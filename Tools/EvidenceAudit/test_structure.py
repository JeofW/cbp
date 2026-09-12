import unittest
from structure import extract_source, validate_graph


class StructureTests(unittest.TestCase):
    def extract(self, source):
        return extract_source('fixture.cs', source.encode())

    def test_comments_and_string_contents_are_not_calls(self):
        graph = self.extract('class A { void F(){ /* Fake(); */ var s="Pretend()"; Real(); } }')
        calls = [e for e in graph['edges'] if e['type'] == 'calls']
        self.assertEqual(len(calls), 1)
        self.assertIn('Real', next(n['label'] for n in graph['nodes'] if n['id'] == calls[0]['target']))

    def test_overloads_are_distinct(self):
        graph = self.extract('class A { void F(){} void F(int x){} }')
        methods = [n for n in graph['nodes'] if n['type'] == 'method']
        self.assertEqual(len(methods), 2)
        self.assertEqual(len({n['id'] for n in methods}), 2)

    def test_calls_reference_syntax_not_unproven_semantic_target(self):
        graph = self.extract('class A { void F(dynamic x){ x.Go(); } }')
        calls = [e for e in graph['edges'] if e['type'] == 'calls']
        ref = next(n for n in graph['nodes'] if n['id'] == calls[0]['target'])
        self.assertEqual(ref['type'], 'method_reference')
        self.assertEqual(calls[0]['resolution'], 'syntax_only')

    def test_event_like_assignment_is_not_assumed_event_binding(self):
        graph = self.extract('class A { void F(){ Events.Start += OnStart; } }')
        edges = [e for e in graph['edges'] if e['type'] == 'subscribes_to']
        self.assertEqual(len(edges), 1)
        self.assertEqual(edges[0]['classification'], 'AMBIGUOUS')
        self.assertTrue(edges[0]['falsification'])

    def test_lambda_call_is_marked_deferred(self):
        graph = self.extract('class A { void F(){ Events.Start += () => Refresh(); } }')
        calls = [e for e in graph['edges'] if e['type'] == 'calls']
        self.assertEqual(calls[0]['execution'], 'deferred')

    def test_all_edges_have_source_line_and_confidence(self):
        graph = self.extract('using X;\nclass A { void F(){ Log.Info("hello"); } }')
        self.assertTrue(graph['edges'])
        validate_graph(graph)
        for edge in graph['edges']:
            self.assertGreaterEqual(edge['provenance'][0]['start_line'], 1)

    def test_broken_graph_is_rejected(self):
        with self.assertRaises(ValueError):
            validate_graph({'nodes': [], 'edges': [{'source': 'missing', 'target': 'missing'}]})

    def test_attributes_are_retained_without_claiming_runtime_discovery(self):
        graph = self.extract('class A { [Behavior(BehaviorType.Combat)] public object F(){return null;} }')
        method = next(n for n in graph['nodes'] if n['type'] == 'method')
        self.assertIn('BehaviorType.Combat', method['attributes'])
        self.assertEqual(method['runtime_validation'], 'not_executed')


if __name__ == '__main__':
    unittest.main()

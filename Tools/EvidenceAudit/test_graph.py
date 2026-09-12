import tempfile
import unittest
from pathlib import Path
import graph
from structure import validate_graph


class GraphTests(unittest.TestCase):
    def fixture(self, directory):
        root = Path(directory)
        for filename, _, last in graph.SOURCES.values():
            path = root / filename
            path.parent.mkdir(parents=True, exist_ok=True)
            count = max(last, len(path.read_text().splitlines()) if path.exists() else 0)
            path.write_text('// fixture\n' * count)
        return root

    def test_each_saved_feedback_query_has_a_closed_directed_path(self):
        with tempfile.TemporaryDirectory() as directory:
            data = graph.build(self.fixture(directory))
            for name, (members, _, _) in graph.LOOPS.items():
                self.assertEqual(members[0], members[-1])
                edges = {(e['source'], e['target']) for e in data['edges'] if e.get('loop') == name}
                self.assertTrue(all((a, b) in edges for a, b in zip(members, members[1:])))
            self.assertEqual(len(graph.LOOPS), 8)

    def test_curated_links_are_not_upgraded_to_extracted_runtime_causation(self):
        with tempfile.TemporaryDirectory() as directory:
            data = graph.build(self.fixture(directory))
            validate_graph(data)
            self.assertTrue(all(e['classification'] == 'INFERRED' for e in data['edges']))
            self.assertTrue(all(e['falsification'] and e['provenance'] for e in data['edges']))

    def test_nonexistent_source_range_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.fixture(directory)
            (root / graph.SOURCES['log'][0]).write_text('short\n')
            with self.assertRaises(ValueError):
                graph.build(root)

    def test_offline_html_escapes_embedded_script_markup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.fixture(directory)
            data = graph.build(root)
            data['nodes'][0]['label'] = '</script><script>alert(1)</script>'
            output = root / 'graph.html'
            graph.write_html(data, output)
            page = output.read_text()
            self.assertNotIn('</script><script>alert(1)', page)
            self.assertNotIn('<script src=', page)
            self.assertNotIn('fetch(', page)
            self.assertIn('textContent', page)


if __name__ == '__main__':
    unittest.main()

import json
import pathlib
import tempfile
import unittest
from compare_routes import FIELDS, compare

class ComparisonTests(unittest.TestCase):
    def test_mutations_and_missing_records_are_rejected(self):
        with tempfile.TemporaryDirectory() as path:
            root = pathlib.Path(path)
            rows = []
            for route in range(5):
                for repeat in range(4):
                    row = {key: False for key in FIELDS}
                    row.update(route=str(route), repeat=repeat, count=0, points=[], flags=[], areas=[],
                               polygons=[], native_call_ms=1.0)
                    rows.append(row)
            names = ('baseline-1', 'indexed-1', 'indexed-2', 'baseline-2')
            for name in names:
                (root/name).mkdir(); (root/name/'routes.json').write_text(json.dumps(rows))
            self.assertEqual(compare(root)['semantic_records_identical'],80)
            for field in ('status','points','flags','polygons','requested_end','out_of_nodes'):
                changed = json.loads(json.dumps(rows)); changed[0][field] = 'changed'
                (root/'indexed-1/routes.json').write_text(json.dumps(changed))
                with self.assertRaises(ValueError): compare(root)
            (root/'indexed-1/routes.json').write_text(json.dumps(rows[:-1]))
            with self.assertRaises(ValueError): compare(root)
            (root/'indexed-1/routes.json').write_text(json.dumps(rows[1:]+[rows[1]]))
            with self.assertRaises(ValueError): compare(root)

if __name__ == '__main__': unittest.main()

"""Test actual native-probe asset preparation; these are not route certificates."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('native_mesh_runner',Path(__file__).resolve().parents[1]/'NativeMeshAudit/run_verified.py')
subject=importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


class NativeMeshLayoutTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.repo=self.root/'repo';(self.repo/'mmaps').mkdir(parents=True)
        self.out=self.root/'runner/mmaps'
        self.name='mmaps/0013431.mmtile';self.raw=b'MMAP'+bytes(range(128))
        (self.repo/self.name).write_bytes(self.raw)
        self.manifest={self.name:{'sha256':hashlib.sha256(self.raw).hexdigest(),'bytes':len(self.raw)}}

    def test_verified_asset_is_placed_beside_dll_without_a_link(self):
        subject.materialize_meshes(self.repo,self.out,self.manifest)
        target=self.out/'0013431.mmtile'
        self.assertEqual(target.read_bytes(),self.raw)
        target.write_bytes(b'controlled missing/corrupted-tile scenario')
        self.assertEqual((self.repo/self.name).read_bytes(),self.raw)

    def test_existing_layout_is_never_overwritten(self):
        self.out.mkdir(parents=True);(self.out/'retained').write_bytes(b'evidence')
        with self.assertRaises(FileExistsError):subject.materialize_meshes(self.repo,self.out,self.manifest)
        self.assertEqual((self.out/'retained').read_bytes(),b'evidence')

    def test_changed_source_bytes_fail_before_destination_creation(self):
        (self.repo/self.name).write_bytes(self.raw+b'changed')
        with self.assertRaises(ValueError):subject.materialize_meshes(self.repo,self.out,self.manifest)
        self.assertFalse(self.out.exists())

    def test_changed_hash_fails_before_destination_creation(self):
        self.manifest[self.name]['sha256']='0'*64
        with self.assertRaises(ValueError):subject.materialize_meshes(self.repo,self.out,self.manifest)
        self.assertFalse(self.out.exists())

    def test_pointer_text_is_not_a_navigation_tile(self):
        pointer=b'version https://git-lfs.github.com/spec/v1\noid sha256:'+b'a'*64+b'\nsize 1234\n'
        (self.repo/self.name).write_bytes(pointer)
        self.manifest[self.name]={'sha256':hashlib.sha256(pointer).hexdigest(),'bytes':len(pointer)}
        with self.assertRaises(ValueError):subject.materialize_meshes(self.repo,self.out,self.manifest)

    def test_foreign_paths_and_invalid_ids_are_rejected(self):
        for name in ('../001.mmap','mmaps/../../settings.xml','mmaps/abc.mmap','mmaps/001.dll','C:/001.mmap'):
            with self.subTest(name=name),self.assertRaises(ValueError):
                subject.materialize_meshes(self.repo,self.out,{name:next(iter(self.manifest.values()))})

    def test_empty_inventory_and_missing_files_are_rejected(self):
        with self.assertRaises(ValueError):subject.materialize_meshes(self.repo,self.out,{})
        (self.repo/self.name).unlink()
        with self.assertRaises(ValueError):subject.materialize_meshes(self.repo,self.out,self.manifest)


if __name__=='__main__':unittest.main()

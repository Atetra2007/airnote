"""Validate the desktop payload, then generate an exact Inno [Files] include.

The .deps.json file describes runtime assets, but its library identities also
have to match this reviewed allowlist. Merely adding a DLL to publish/ is never
enough to get it into the installer. User songs and upstream binaries are absent.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PRODUCT = 'HarmonicaDesk'
PROJECTS = {'HarmonicaDesk', 'Harmonica.Core', 'Harmonica.Windows'}
RUNTIME_PACKS = {
    'Microsoft.NETCore.App.Runtime.win-x64',
    'Microsoft.WindowsDesktop.App.Runtime.win-x64',
}
SOURCE_FILES = {
    'Resources/catalog.json': 'catalog/songs.json',
    'Resources/Examples/01-timing-check.txt': 'scores/original/01-timing-check.txt',
    'Resources/Examples/02-stop-check.txt': 'scores/original/02-stop-check.txt',
    'Resources/Examples/03-import-save-check.txt': 'scores/original/03-import-save-check.txt',
    'Licenses/project-MIT.txt': 'LICENSE',
    'Licenses/THIRD_PARTY.md': 'THIRD_PARTY.md',
}
CORE_FILES = {PRODUCT + extension for extension in ('.exe', '.dll', '.deps.json', '.runtimeconfig.json')}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def safe_relative(name: str) -> str:
    require('\\' not in name and ':' not in name and '"' not in name and '\n' not in name and '\r' not in name,
            f'Unsafe payload path: {name!r}')
    path = PurePosixPath(name)
    require(not path.is_absolute() and bool(path.parts) and all(part not in {'..', '.'} for part in path.parts),
            f'Unsafe payload path: {name!r}')
    require(str(path) == name, f'Non-canonical payload path: {name!r}')
    return name


def dependency_files(deps: dict) -> tuple[set[str], list[tuple[str, str]]]:
    target_name = deps['runtimeTarget']['name']
    require(target_name.endswith('/win-x64'), 'Payload must target win-x64.')
    target = deps['targets'][target_name]
    libraries = deps['libraries']
    allowed: set[str] = set(CORE_FILES)
    runtime_packs = []
    for identity, assets in target.items():
        name, version = identity.rsplit('/', 1)
        kind = libraries[identity]['type']
        pack_name = name.removeprefix('runtimepack.')
        is_project = kind == 'project' and name in PROJECTS
        is_pack = kind == 'runtimepack' and pack_name in RUNTIME_PACKS
        require(is_project or is_pack, f'Unreviewed dependency: {identity} ({kind})')
        if is_pack:
            require(version.startswith('10.0.'), f'Unexpected runtime version: {identity}')
            runtime_packs.append((pack_name, version))
        for section in ('runtime', 'native', 'resources', 'runtimeTargets'):
            for asset, metadata in assets.get(section, {}).items():
                safe_relative(asset)
                if PurePosixPath(asset).name == '_._':
                    continue
                if section == 'runtimeTargets' and metadata.get('rid') != 'win-x64':
                    continue
                filename = PurePosixPath(asset).name
                if is_project:
                    require(section == 'runtime' and filename == name + '.dll',
                            f'Unreviewed project output: {identity}/{asset}')
                if section == 'resources':
                    culture = metadata.get('locale') or PurePosixPath(asset).parent.name
                    filename = f'{culture}/{filename}'
                safe_relative(filename)
                allowed.add(filename)
    require({name for name, _ in runtime_packs} == RUNTIME_PACKS,
            'Both .NET and WindowsDesktop runtime packs must be self-contained.')
    return allowed, sorted(runtime_packs)


def runtime_package_dirs(packs: list[tuple[str, str]], root: Path) -> dict[str, Path]:
    assets_file = root / 'src/Harmonica.Desktop/obj/project.assets.json'
    assets = json.loads(assets_file.read_text(encoding='utf-8'))
    folders = [Path(folder) for folder in assets['packageFolders']]
    result = {}
    for name, version in packs:
        locations = [folder / name.lower() / version for folder in folders]
        package = next((folder for folder in locations if folder.is_dir()), None)
        require(package is not None, f'Runtime package missing from NuGet cache: {name}/{version}')
        result[name] = package
    return result


def runtime_license_sources(packs: list[tuple[str, str]], root: Path) -> dict[str, Path]:
    result: dict[str, Path] = {}
    for name, package in runtime_package_dirs(packs, root).items():
        entries = {path.name.upper().replace('-', ''): path for path in package.iterdir() if path.is_file()}
        filenames = ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') if name == 'Microsoft.NETCore.App.Runtime.win-x64' else ('LICENSE.TXT',)
        for filename in filenames:
            source = entries.get(filename.replace('-', ''))
            if filename == 'LICENSE.TXT':
                source = source or entries.get('LICENSE')
            require(source is not None, f'{name} lacks {filename}; review package licensing.')
            result[f'Licenses/{name}.{filename}'] = source
    return result


def runtime_resource_sources(packs: list[tuple[str, str]], root: Path, declared: set[str]) -> dict[str, Path]:
    # Self-contained .deps.json deliberately omits framework satellites. Their
    # exact source list is supplied by the restored runtime pack, not a wildcard.
    result: dict[str, Path] = {}
    for name, package in runtime_package_dirs(packs, root).items():
        manifest = ET.parse(package / 'data/RuntimeList.xml').getroot()
        for file in manifest.findall('File'):
            if file.get('Type') != 'Resources':
                continue
            profiles = file.get('Profile', '').split(';')
            if name == 'Microsoft.WindowsDesktop.App.Runtime.win-x64' and profiles != [''] and 'WPF' not in profiles:
                continue
            source_path = safe_relative(file.attrib['Path'])
            owner = PurePosixPath(source_path).name.removesuffix('.resources.dll') + '.dll'
            if owner not in declared:
                continue
            culture = file.attrib['Culture']
            output_path = safe_relative(f'{culture}/{PurePosixPath(source_path).name}')
            result[output_path] = package / source_path
    return result


def copy_licenses(publish: Path, root: Path) -> None:
    require(publish.is_dir() and not publish.is_symlink(), 'Publish directory is missing or linked.')
    deps = json.loads((publish / f'{PRODUCT}.deps.json').read_text(encoding='utf-8'))
    _, packs = dependency_files(deps)
    sources = {name: root / source for name, source in SOURCE_FILES.items() if name.startswith('Licenses/')}
    sources.update(runtime_license_sources(packs, root))
    for name, source in sources.items():
        destination = publish / name
        require(not destination.parent.is_symlink() and not destination.is_symlink(), 'License target must not be linked.')
        if destination.parent.exists():
            require(not bool(getattr(destination.parent.lstat(), 'st_file_attributes', 0) & 0x400),
                    'License directory must not be a junction.')
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(source.read_bytes())


def validate_payload(publish: Path, root: Path = ROOT) -> list[dict]:
    require(publish.is_dir() and not publish.is_symlink(), 'Publish directory does not exist or is a symlink.')
    deps = json.loads((publish / f'{PRODUCT}.deps.json').read_text(encoding='utf-8'))
    allowed, packs = dependency_files(deps)
    sources = {name: root / source for name, source in SOURCE_FILES.items()}
    sources.update(runtime_license_sources(packs, root))
    sources.update(runtime_resource_sources(packs, root, allowed))
    allowed.update(sources)
    actual: dict[str, Path] = {}
    for path in publish.rglob('*'):
        reparse_point = bool(getattr(path.lstat(), 'st_file_attributes', 0) & 0x400)
        require(not path.is_symlink() and not reparse_point, f'Linked path in payload: {path}')
        require(path.resolve().is_relative_to(publish.resolve()), f'Path escapes publish directory: {path}')
        if path.is_file():
            name = safe_relative(path.relative_to(publish).as_posix())
            actual[name] = path
    require(len({name.casefold() for name in actual}) == len(actual), 'Case-insensitive path collision.')
    require(not set(actual) - allowed, f'Unreviewed files in publish: {sorted(set(actual) - allowed)}')
    require(not allowed - set(actual), f'Missing runtime or resource files: {sorted(allowed - set(actual))}')
    for name, source in sources.items():
        require(actual[name].read_bytes() == source.read_bytes(), f'Resource differs from reviewed source: {name}')
    runtime = json.loads(actual[f'{PRODUCT}.runtimeconfig.json'].read_text(encoding='utf-8'))['runtimeOptions']
    require(runtime.get('tfm') == 'net10.0', 'Unexpected target framework.')
    require('framework' not in runtime and 'frameworks' not in runtime, 'Payload depends on an installed framework.')
    require(bool(runtime.get('includedFrameworks')), 'Self-contained framework metadata is missing.')
    return [
        {'path': name, 'size': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
        for name, path in sorted(actual.items())
    ]


def write_outputs(publish: Path, records: list[dict], manifest: Path, include: Path) -> None:
    manifest.parent.mkdir(parents=True, exist_ok=True)
    include.parent.mkdir(parents=True, exist_ok=True)
    version = ET.parse(ROOT / 'src/Harmonica.Desktop/Harmonica.Desktop.csproj').findtext('.//Version')
    require(bool(version), 'Product version is missing.')
    manifest.write_text(json.dumps({'schema_version': 1, 'product': PRODUCT, 'version': version,
                                    'files': records}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    lines = ['; Generated only after publication boundary checks. Do not edit.']
    for record in records:
        path = PurePosixPath(record['path'])
        relative = str(path).replace('/', '\\')
        parent = str(path.parent).replace('/', '\\')
        destination = '{app}' if parent == '.' else '{app}\\' + parent
        lines.append(f'Source: "{{#PublishDir}}\\{relative}"; DestDir: "{destination}"; Flags: ignoreversion')
    include.write_text('\n'.join(lines) + '\n', encoding='utf-8-sig')


def self_test() -> None:
    for name in ('../private.txt', '/outside', 'C:/secret', 'x\\private.txt', 'a/../b', 'x\n.txt', '"quoted'):
        try:
            safe_relative(name)
        except ValueError:
            pass
        else:
            raise AssertionError(f'Unsafe path accepted: {name}')
    require(safe_relative('Resources/Examples/01-timing-check.txt') == 'Resources/Examples/01-timing-check.txt',
            'Safe path rejected.')
    target = '.NETCoreApp,Version=v10.0/win-x64'
    fixture = {'runtimeTarget': {'name': target}, 'targets': {target: {}}, 'libraries': {}}
    for name in RUNTIME_PACKS:
        identity = f'runtimepack.{name}/10.0.8'
        fixture['targets'][target][identity] = {'runtime': {f'runtimes/win-x64/lib/net10.0/{name}.dll': {}}}
        fixture['libraries'][identity] = {'type': 'runtimepack'}
    allowed, packs = dependency_files(fixture)
    require(len(packs) == 2 and 'Microsoft.NETCore.App.Runtime.win-x64.dll' in allowed, 'Runtime assets not expanded.')
    fixture['targets'][target]['Foreign.Binary/1.0.0'] = {'runtime': {'Foreign.Binary.dll': {}}}
    fixture['libraries']['Foreign.Binary/1.0.0'] = {'type': 'package'}
    try:
        dependency_files(fixture)
    except ValueError:
        pass
    else:
        raise AssertionError('Unknown dependency accepted.')
    del fixture['targets'][target]['Foreign.Binary/1.0.0']
    del fixture['libraries']['Foreign.Binary/1.0.0']
    with tempfile.TemporaryDirectory(prefix='harmonica-manifest-') as directory:
        root = Path(directory)
        publish = root / 'publish'
        publish.mkdir()
        package_cache = root / 'nuget'
        for name in RUNTIME_PACKS:
            package = package_cache / name.lower() / '10.0.8'
            package.mkdir(parents=True)
            (package / 'data').mkdir()
            (package / 'data/RuntimeList.xml').write_text('<FileList />', encoding='utf-8')
            for notice in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
                (package / notice).write_text('test runtime license', encoding='utf-8')
            (publish / f'{name}.dll').write_bytes(b'test runtime')
        assets_file = root / 'src/Harmonica.Desktop/obj/project.assets.json'
        assets_file.parent.mkdir(parents=True)
        assets_file.write_text(json.dumps({'packageFolders': {str(package_cache): {}}}), encoding='utf-8')
        for destination, source_name in SOURCE_FILES.items():
            source = root / source_name
            source.parent.mkdir(parents=True, exist_ok=True)
            source.write_text('original test resource', encoding='utf-8')
            if not destination.startswith('Licenses/'):
                output = publish / destination
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_bytes(source.read_bytes())
        (publish / f'{PRODUCT}.exe').write_bytes(b'test apphost')
        (publish / f'{PRODUCT}.dll').write_bytes(b'test app')
        (publish / f'{PRODUCT}.deps.json').write_text(json.dumps(fixture), encoding='utf-8')
        (publish / f'{PRODUCT}.runtimeconfig.json').write_text(
            json.dumps({'runtimeOptions': {'tfm': 'net10.0', 'includedFrameworks': [{'name': 'test'}]}}), encoding='utf-8')
        copy_licenses(publish, root)
        records = validate_payload(publish, root)
        require(bool(records), 'Complete valid payload was rejected.')
        personal_file = publish / 'private-song.txt'
        personal_file.write_text('private', encoding='utf-8')
        try:
            validate_payload(publish, root)
        except ValueError as error:
            require('Unreviewed files' in str(error), 'Unexpected rejection reason for personal song.')
        else:
            raise AssertionError('Personal song was allowed into the installer.')
        personal_file.unlink()
        (publish / 'Resources/catalog.json').write_text('changed source metadata', encoding='utf-8')
        try:
            validate_payload(publish, root)
        except ValueError as error:
            require('differs from reviewed source' in str(error), 'Unexpected rejection reason for modified metadata.')
        else:
            raise AssertionError('Modified source metadata was allowed into the installer.')
        write_outputs(root, [{'path': 'Resources/Examples/01-timing-check.txt', 'size': 1, 'sha256': '0' * 64}],
                      root / 'manifest.json', root / 'files.iss')
        text = (root / 'files.iss').read_text(encoding='utf-8-sig')
        require('*' not in text and 'recurse' not in text, 'Installer file list must not contain wildcards.')
    print('PASS: valid payload accepted; unsafe paths, foreign dependencies, personal songs and modified resources rejected; exact Inno file list generated.')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', type=Path, default=ROOT / 'artifacts/desktop/publish')
    parser.add_argument('--copy-licenses', action='store_true')
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--inno-include', type=Path)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if args.copy_licenses:
        copy_licenses(args.publish, ROOT)
    records = validate_payload(args.publish)
    require(bool(args.manifest) == bool(args.inno_include), 'Specify both --manifest and --inno-include.')
    if args.manifest:
        write_outputs(args.publish, records, args.manifest, args.inno_include)
    print(f'PASS: {len(records)} explicitly reviewed desktop files; no upstream player or personal scores.')


if __name__ == '__main__':
    main()

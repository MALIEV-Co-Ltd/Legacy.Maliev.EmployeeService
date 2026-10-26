"""Exact ordinary EF13 source successor. No native or owner authority."""
import atexit
import contextlib
import hashlib
import json
from pathlib import Path
import tempfile
import employee_artifact_transport_profile as transport

# BEGIN GENERATED CLOSED PROFILE
MAIN = '5f15cd29dabaf25e542039f6e39e15a8213e6327'
ARCHIVES = {'Directory.Build.props': {'archive': 'scripts/dependency-predecessors/main5f-00.bin',
                           'blob': 'b47c8d23651f11ea8366bb63f4520fb8de6c6df3',
                           'bytes': 515,
                           'sha256': 'b03b93e505f9bd0f5d396c5041d9b35d6cfccb454384ba8151e55e05edf45083'},
 'Legacy.Maliev.EmployeeService.Api/Legacy.Maliev.EmployeeService.Api.csproj': {'archive': 'scripts/dependency-predecessors/main5f-01.bin',
                                                                                'blob': '4198649b29c8b5260e37cd03ff312a8db3cfff2b',
                                                                                'bytes': 3008,
                                                                                'sha256': '07326d6bfcc3501cbc1fbd7838b31c85191bfc3f297179fb4677aea8c8a6106a'},
 'Legacy.Maliev.EmployeeService.Data/Legacy.Maliev.EmployeeService.Data.csproj': {'archive': 'scripts/dependency-predecessors/main5f-02.bin',
                                                                                  'blob': '2a670ed11a288fc73f65344cedcb48055afe7fe2',
                                                                                  'bytes': 1170,
                                                                                  'sha256': '9a2a6618927467a2abf194f72e5adc91a12c8d0c3842b2b4b720ccea330c89a3'},
 'Legacy.Maliev.EmployeeService.Tests/EmployeeScaffoldRuntimeProof.cs': {'archive': 'scripts/dependency-predecessors/main5f-03.bin',
                                                                         'blob': 'de3666bf2716b65b452449cf715cc9bd17d69a0d',
                                                                         'bytes': 26237,
                                                                         'sha256': 'b865e2d071daecc8fadf2714d08efa95d1a041f1081efd1277522f649daeb07f'},
 'Legacy.Maliev.EmployeeService.Tests/Workflows/WorkflowContractTests.cs': {'archive': 'scripts/dependency-predecessors/main5f-04.bin',
                                                                            'blob': '45f046f70f50c7fcfef01e5895476339bd41feed',
                                                                            'bytes': 22393,
                                                                            'sha256': 'd868ac9911bab66380edd31fa2c3693bf62094fbe83390539e9838dec9c607cb'},
 'scripts/employee-metadata-source-successor.json': {'archive': 'scripts/dependency-predecessors/main5f-05.bin',
                                                     'blob': '7fca9132e3f8507e5712cd95e3094d14846b77fc',
                                                     'bytes': 175392,
                                                     'sha256': '10550cd1c29de6279e5daa0669bf9a8bdc18a5dd9f04962c82dfe542e4fce77c'},
 'scripts/employee-pagination-focused-manifest.json': {'archive': 'scripts/dependency-predecessors/main5f-06.bin',
                                                       'blob': '4cb4073a1269f883f6c396056a6adbd88202e144',
                                                       'bytes': 81339,
                                                       'sha256': '78abbc26b74b8854cff5dbc13cb71607619067c8e0b7253be19fde589e017b32'},
 'scripts/employee_artifact_transport_profile.py': {'archive': 'scripts/dependency-predecessors/main5f-07.bin',
                                                    'blob': '227a4ff556f4acc1139f6798936ca354fe7e4273',
                                                    'bytes': 9396,
                                                    'sha256': 'f9b6397773af87a557fb2162f74b7799c983e3b6121dc3946605ff9aab37c152'},
 'scripts/test_employee_metadata_proof.py': {'archive': 'scripts/dependency-predecessors/main5f-08.bin',
                                             'blob': 'f5bed6b3a0c3df0feb80d3ecfe83725506cc33c9',
                                             'bytes': 44724,
                                             'sha256': '321f05bb6d9461bba41a52561aae12a8e384f9cba4cedd9b56731552f0bf603c'},
 'scripts/test_employee_pagination_focused_validator.py': {'archive': 'scripts/dependency-predecessors/main5f-09.bin',
                                                           'blob': 'f3ae2ddcfc020883f7dc05a414db8292904089db',
                                                           'bytes': 16161,
                                                           'sha256': 'b2500a824600e88cd3c5a16eec78570ec2ba5df151443da826f3ddb3b34084d9'},
 'scripts/verify_employee_address_input_proof.py': {'archive': 'scripts/dependency-predecessors/main5f-10.bin',
                                                    'blob': '6a9dfa44bd61e3f76098147e1c72aed570dbfb58',
                                                    'bytes': 22091,
                                                    'sha256': '18040dc2f18178ca9d9508ac948b066e2b6bda94e8eb838d48da3dda5e5316c0'},
 'scripts/verify_employee_metadata_proof.py': {'archive': 'scripts/dependency-predecessors/main5f-11.bin',
                                               'blob': 'ca97645ef6d3441123726b7689ed1f6bc3eb2b3f',
                                               'bytes': 28228,
                                               'sha256': '2bc98b97de2b8a9622a38222635442e8958699252737168640b1fe254eb4fdb8'},
 'scripts/verify_employee_pagination_focused.py': {'archive': 'scripts/dependency-predecessors/main5f-12.bin',
                                                   'blob': '475804f75dbbe49b07bec93b35465198cbd9bc1f',
                                                   'bytes': 9830,
                                                   'sha256': 'd1c28a01d050e9fbaefcef7d0450892a39233cdfb251be82f5f99fbcdf9bbb21'}}
TRANSFORMS = {'Directory.Build.props': [{'after': '    <PackageReference Update="Microsoft.EntityFrameworkCore" '
                                     'Version="10.0.13" />\n'
                                     '    <PackageReference '
                                     'Update="Microsoft.EntityFrameworkCore.Relational" Version="10.0.13" '
                                     '/>\n'
                                     '    <PackageReference '
                                     'Update="Microsoft.EntityFrameworkCore.Abstractions" Version="10.0.13" '
                                     '/>\n',
                            'before': '    <PackageReference Update="Microsoft.EntityFrameworkCore" '
                                      'Version="10.0.12" />\n'
                                      '    <PackageReference '
                                      'Update="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" '
                                      '/>\n'
                                      '    <PackageReference '
                                      'Update="Microsoft.EntityFrameworkCore.Abstractions" Version="10.0.12" '
                                      '/>\n',
                            'oldLineCount': 3,
                            'startLine': 7}],
 'Legacy.Maliev.EmployeeService.Api/Legacy.Maliev.EmployeeService.Api.csproj': [{'after': '    '
                                                                                          '<PackageReference '
                                                                                          'Include="Microsoft.EntityFrameworkCore" '
                                                                                          'Version="10.0.13" '
                                                                                          '/>\n'
                                                                                          '    '
                                                                                          '<PackageReference '
                                                                                          'Include="Microsoft.EntityFrameworkCore.Relational" '
                                                                                          'Version="10.0.13" '
                                                                                          '/>\n',
                                                                                 'before': '',
                                                                                 'oldLineCount': 0,
                                                                                 'startLine': 33},
                                                                                {'after': '    '
                                                                                          '<PackageReference '
                                                                                          'Include="Microsoft.EntityFrameworkCore.Design" '
                                                                                          'Version="10.0.13" '
                                                                                          'Condition="\'$(EnableEmployeeScaffoldDesignTime)\' '
                                                                                          '== \'true\'">\n',
                                                                                 'before': '    '
                                                                                           '<PackageReference '
                                                                                           'Include="Microsoft.EntityFrameworkCore.Design" '
                                                                                           'Version="10.0.12" '
                                                                                           'Condition="\'$(EnableEmployeeScaffoldDesignTime)\' '
                                                                                           '== \'true\'">\n',
                                                                                 'oldLineCount': 1,
                                                                                 'startLine': 36}],
 'Legacy.Maliev.EmployeeService.Data/Legacy.Maliev.EmployeeService.Data.csproj': [{'after': '    '
                                                                                            '<PackageReference '
                                                                                            'Include="Microsoft.EntityFrameworkCore.Design" '
                                                                                            'Version="10.0.13">\n',
                                                                                   'before': '    '
                                                                                             '<PackageReference '
                                                                                             'Include="Microsoft.EntityFrameworkCore.Design" '
                                                                                             'Version="10.0.12">\n',
                                                                                   'oldLineCount': 1,
                                                                                   'startLine': 9},
                                                                                  {'after': '    '
                                                                                            '<PackageReference '
                                                                                            'Include="Microsoft.Extensions.Caching.Abstractions" '
                                                                                            'Version="10.0.13" '
                                                                                            '/>\n'
                                                                                            '    '
                                                                                            '<PackageReference '
                                                                                            'Include="Microsoft.Extensions.Logging.Abstractions" '
                                                                                            'Version="10.0.13" '
                                                                                            '/>\n',
                                                                                   'before': '    '
                                                                                             '<PackageReference '
                                                                                             'Include="Microsoft.Extensions.Caching.Abstractions" '
                                                                                             'Version="10.0.12" '
                                                                                             '/>\n'
                                                                                             '    '
                                                                                             '<PackageReference '
                                                                                             'Include="Microsoft.Extensions.Logging.Abstractions" '
                                                                                             'Version="10.0.12" '
                                                                                             '/>\n',
                                                                                   'oldLineCount': 2,
                                                                                   'startLine': 13}],
 'Legacy.Maliev.EmployeeService.Tests/EmployeeScaffoldRuntimeProof.cs': [{'after': '            await '
                                                                                   'ExecuteAsync("dotnet", '
                                                                                   '["tool", "install", '
                                                                                   '"dotnet-ef", '
                                                                                   '"--version", "10.0.13", '
                                                                                   '"--tool-path", tools], '
                                                                                   '"exact-tool-install", '
                                                                                   'repository, environment, '
                                                                                   'phases);\n',
                                                                          'before': '            await '
                                                                                    'ExecuteAsync("dotnet", '
                                                                                    '["tool", "install", '
                                                                                    '"dotnet-ef", '
                                                                                    '"--version", "10.0.12", '
                                                                                    '"--tool-path", tools], '
                                                                                    '"exact-tool-install", '
                                                                                    'repository, '
                                                                                    'environment, phases);\n',
                                                                          'oldLineCount': 1,
                                                                          'startLine': 65},
                                                                         {'after': '            '
                                                                                   'Assert.Equal("10.0.13", '
                                                                                   'designers[0].GetProperty("Version").GetString());\n',
                                                                          'before': '            '
                                                                                    'Assert.Equal("10.0.12", '
                                                                                    'designers[0].GetProperty("Version").GetString());\n',
                                                                          'oldLineCount': 1,
                                                                          'startLine': 300},
                                                                         {'after': '            '
                                                                                   '<PackageReference '
                                                                                   'Include="Microsoft.EntityFrameworkCore" '
                                                                                   'Version="10.0.13" />\n'
                                                                                   '            '
                                                                                   '<PackageReference '
                                                                                   'Include="Microsoft.EntityFrameworkCore.Relational" '
                                                                                   'Version="10.0.13" />\n'
                                                                                   '            '
                                                                                   '<PackageReference '
                                                                                   'Include="Microsoft.EntityFrameworkCore.Abstractions" '
                                                                                   'Version="10.0.13" />\n',
                                                                          'before': '            '
                                                                                    '<PackageReference '
                                                                                    'Include="Microsoft.EntityFrameworkCore" '
                                                                                    'Version="10.0.12" />\n'
                                                                                    '            '
                                                                                    '<PackageReference '
                                                                                    'Include="Microsoft.EntityFrameworkCore.Relational" '
                                                                                    'Version="10.0.12" />\n'
                                                                                    '            '
                                                                                    '<PackageReference '
                                                                                    'Include="Microsoft.EntityFrameworkCore.Abstractions" '
                                                                                    'Version="10.0.12" />\n',
                                                                          'oldLineCount': 3,
                                                                          'startLine': 329}],
 'Legacy.Maliev.EmployeeService.Tests/Workflows/WorkflowContractTests.cs': [{'after': '        '
                                                                                      'Assert.Equal("10.0.13", '
                                                                                      'reference.Attribute("Version")?.Value);\n',
                                                                             'before': '        '
                                                                                       'Assert.Equal("10.0.12", '
                                                                                       'reference.Attribute("Version")?.Value);\n',
                                                                             'oldLineCount': 1,
                                                                             'startLine': 89}],
 'scripts/test_employee_metadata_proof.py': [{'after': 'from employee_dependency_profile import '
                                                       'historical_scripts\n'
                                                       'from test_employee_dependency_profile import '
                                                       'DependencyProfileControls\n'
                                                       'HERE = historical_scripts(__file__)\n',
                                              'before': 'HERE = Path(__file__).resolve().parent\n',
                                              'oldLineCount': 1,
                                              'startLine': 14}],
 'scripts/test_employee_pagination_focused_validator.py': [{'after': 'from employee_dependency_profile '
                                                                     'import historical_scripts\n'
                                                                     'from test_employee_dependency_profile '
                                                                     'import DependencyProfileControls\n'
                                                                     'HERE=historical_scripts(__file__)\n',
                                                            'before': 'HERE=Path(__file__).resolve().parent\n',
                                                            'oldLineCount': 1,
                                                            'startLine': 5}],
 'scripts/verify_employee_address_input_proof.py': [{'after': '    shared.source_inventory(ROOT, '
                                                              'shared.expanded_rows(custody), exclusions, '
                                                              'shared.checkout_contracts(custody))\n',
                                                     'before': '    shared.source_inventory(ROOT, '
                                                               'custody["candidate"], exclusions, '
                                                               'shared.checkout_contracts(custody))\n',
                                                     'oldLineCount': 1,
                                                     'startLine': 315}],
 'scripts/verify_employee_metadata_proof.py': [{'after': 'from employee_dependency_profile import '
                                                         'authenticate_metadata, expanded_rows\n',
                                                'before': 'from employee_artifact_transport_profile import '
                                                          'authenticate_metadata\n',
                                                'oldLineCount': 1,
                                                'startLine': 7},
                                               {'after': '    count = source_inventory(root, '
                                                         'expanded_rows(manifest), excluded, '
                                                         'checkout_contracts(manifest))\n',
                                                'before': '    count = source_inventory(root, '
                                                          'manifest["candidate"], excluded, '
                                                          'checkout_contracts(manifest))\n',
                                                'oldLineCount': 1,
                                                'startLine': 159},
                                               {'after': '    candidate_bytes = source_inventory(ROOT, '
                                                         'expanded_rows(manifest), excluded, '
                                                         'checkout_contracts(manifest))\n',
                                                'before': '    candidate_bytes = source_inventory(ROOT, '
                                                          'manifest["candidate"], excluded, '
                                                          'checkout_contracts(manifest))\n',
                                                'oldLineCount': 1,
                                                'startLine': 252}],
 'scripts/verify_employee_pagination_focused.py': [{'after': 'from employee_dependency_profile import '
                                                             'authenticate_pagination\n',
                                                    'before': 'from employee_artifact_transport_profile '
                                                              'import authenticate_pagination\n',
                                                    'oldLineCount': 1,
                                                    'startLine': 5}]}
CONTROL_SHA256 = '6e56c5f69dba1eb34bb2327769d2cd58eb04dc11e6aa74a794fdafe360b7d4b7'
# END GENERATED CLOSED PROFILE

FIELD = 'dependencySourceSuccessor'
METADATA = transport.METADATA
PAGINATION = transport.PAGINATION
MODULE = 'scripts/employee_dependency_profile.py'
CONTROLS = 'scripts/test_employee_dependency_profile.py'
VIEWS = []

def need(value, reason):
    if not value: raise ValueError('Dependency profile: ' + reason)

def exact(left, right):
    return json.dumps(left, sort_keys=True, separators=(',', ':')) == json.dumps(right, sort_keys=True, separators=(',', ':'))

def read(root, relative):
    # Preserve the accepted 1MiB bound and symlink/junction/hardlink and
    # lstat/open/fstat/post-read identity checks without duplicating them.
    return transport.read(root, relative)

# Detect replacement after this reviewed verifier was imported. This does not
# authenticate an arbitrary replacement verifier at a fresh import; exact Git
# source/review custody remains the outer trust boundary.
_LOADED_MODULE_BYTES = read(Path(__file__).resolve().parents[1], MODULE)

def identity(path, raw, digest='blob'):
    value = hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest() if digest == 'blob' else hashlib.sha256(raw).hexdigest()
    return dict(path=path, bytes=len(raw), **{digest:value})

def archive(root, path):
    row = ARCHIVES[path]
    raw = read(root, row['archive'])
    need(len(raw) == row['bytes'] and hashlib.sha256(raw).hexdigest() == row['sha256']
         and identity(path, raw)['blob'] == row['blob'], 'immutable main5f predecessor: '+path)
    return raw

def predecessor(root, source):
    need(source in (METADATA, PAGINATION), 'known profile')
    return transport.object_json(archive(root, source))

def transformed(root, path):
    raw = archive(root, path)
    # Fixed line positions authenticate insertions as well as replacements.
    lines = raw.splitlines(keepends=True)
    for edit in reversed(TRANSFORMS[path]):
        at = edit['startLine']-1
        need(b''.join(lines[at:at+edit['oldLineCount']]) == edit['before'].encode(), 'exact source preimage')
        lines[at:at+edit['oldLineCount']] = [edit['after'].encode()]
    return b''.join(lines)

def ordered(rows):
    need(type(rows) is list, 'ordered source rows')
    result = {}
    for row in rows:
        need(type(row) is dict and isinstance(row.get('path'),str) and row['path'] not in result, 'unique source rows')
        result[row['path']] = row
    return result

def extra_rows(root, digest, before):
    names = sorted({MODULE, CONTROLS} | {row['archive'] for row in ARCHIVES.values()} | (set(TRANSFORMS)-set(before)))
    return [identity(name, read(root,name),digest) for name in names]

def expanded_rows(manifest):
    # Real guard always authenticates before this call. No historical fallback.
    need(FIELD in manifest, 'current dependency profile required')
    return manifest['candidate'] + manifest[FIELD]['additionalInputs']

def validate(root, manifest, source, stack=()):
    need(source in (METADATA,PAGINATION) and source not in stack, 'acyclic known manifest')
    need(type(manifest) is dict and FIELD in manifest, 'reviewed dependency profile required')
    need(read(root,MODULE)==_LOADED_MODULE_BYTES, 'loaded dependency verifier changed')
    need(exact(manifest,transport.object_json(read(root,source))), 'actual manifest bytes required')
    old = predecessor(root,source)
    key,digest = ('candidate','blob') if source == METADATA else ('codePins','sha256')
    need(exact({k:v for k,v in manifest.items() if k not in (key,FIELD)},
               {k:v for k,v in old.items() if k != key}), 'historical fields, grants and artifact profile unchanged')
    before,current = ordered(old[key]),ordered(manifest[key])
    need(list(before)==list(current), 'original ordered roster required')
    # Validate every permitted source edit, including hooks unlisted by pagination.
    for path in TRANSFORMS:
        need(read(root,path)==transformed(root,path), 'unreviewed source transform: '+path)
    need(read(root,'scripts/employee_artifact_transport_profile.py') == archive(root,'scripts/employee_artifact_transport_profile.py'),
         'accepted transport authenticator unchanged')
    need(hashlib.sha256(read(root,CONTROLS)).hexdigest()==CONTROL_SHA256, 'exact reviewed dependency controls')
    changes=[]
    for path,row in before.items():
        expected=row
        if path in TRANSFORMS: expected=identity(path,transformed(root,path),digest)
        elif path == PAGINATION:
            page=transport.object_json(read(root,PAGINATION))
            validate(root,page,PAGINATION,stack+(source,))
            expected=identity(path,read(root,path),digest)
        need(exact(current[path],expected), 'undeclared binding change: '+path)
        if not exact(row,expected): changes.append(dict(path=path,before=row,after=expected))
    for path in ARCHIVES: archive(root,path)
    expected=dict(profile='employee-ef13-main5f',predecessorCommit=MAIN,
        predecessorManifest=dict(path=source,bytes=ARCHIVES[source]['bytes'],sha256=ARCHIVES[source]['sha256'],blob=ARCHIVES[source]['blob']),
        changedBindings=changes,additionalInputs=extra_rows(root,digest,before))
    need(exact(manifest[FIELD],expected), 'closed dependency profile and additional bindings')
    return old

@contextlib.contextmanager
def _projection(root):
    old=predecessor(root,METADATA)
    parent=Path(root).resolve().parent
    with tempfile.TemporaryDirectory(prefix='.employee-dependency-main5f-',dir=parent) as temporary:
        target=Path(temporary)
        need(target.resolve().parent==parent, 'owned historical view')
        for row in old['candidate'] + [dict(path=METADATA)]:
            name=row['path']
            raw=archive(root,name) if name in ARCHIVES else read(root,name)
            if name != METADATA:
                canonical=raw
                contract=next((r for r in old['checkoutRepresentations'] if r['path']==name),None)
                if contract and len(raw)==contract['checkoutBytes'] and identity(name,raw)['blob']==contract['checkoutBlob']:
                    canonical=raw.replace(b'\r\n',b'\n')
                need(exact(identity(name,canonical),row), 'bound historical source required: '+name)
            dest=target/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(raw)
        yield target

@contextlib.contextmanager
def historical_view(root):
    validate(root,transport.object_json(read(root,METADATA)),METADATA)
    validate(root,transport.object_json(read(root,PAGINATION)),PAGINATION)
    with _projection(root) as history:
        yield history

def authenticate_metadata(root, manifest):
    old=validate(root,manifest,METADATA)
    with _projection(root) as history:
        transport.authenticate_metadata(history,old)
        transport.authenticate_pagination(history,predecessor(root,PAGINATION))
    return old

def authenticate_pagination(root, manifest):
    old=validate(root,manifest,PAGINATION)
    metadata=transport.object_json(read(root,METADATA))
    authenticate_metadata(root,metadata)
    row=next(row for row in metadata['candidate'] if row['path']==PAGINATION)
    need(exact(row,identity(PAGINATION,read(root,PAGINATION))), 'mixed metadata/pagination')
    return old

def historical_scripts(script):
    # Only pure test modules call this; real source admission never accepts a
    # historical-only root. Keep the view alive for unittest discovery/execution.
    root=Path(script).resolve().parents[1]
    metadata=transport.object_json(read(root,METADATA))
    authenticate_metadata(root,metadata)
    owner=historical_view(root)
    history=owner.__enter__()
    VIEWS.append(owner)
    # Register after TemporaryDirectory's weakref finalizer so explicit closure
    # runs first even when this is the first temporary object in the process.
    atexit.unregister(close_views)
    atexit.register(close_views)
    return history/'scripts'

def close_views():
    while VIEWS: VIEWS.pop().__exit__(None,None,None)

atexit.register(close_views)

"""Employee fixed raw-source intake using the accepted immutable capsule decoder.

Materialization is source custody only. There is no SDK command or native grant.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil

REPOSITORY = 'MALIEV-Co-Ltd/Legacy.Maliev.EmployeeService'
BASE = 'bb7333e8e6153b19de394fb3c45925c6dc948934'
MODULE_SHA256 = '44a8a5accac9da11422d606be02fe28487642215df511b5f1c4284296a453ee2'
POLICY_SHA256 = '88507d27eeee6b7b4095bb556a5c7d1fc2d1b859786d0d5ba891c5bada0be895'
COUNTS = {'baseline': 103, 'candidate': 103, 'dependencies': 151}
DEPENDENCIES = {'Legacy.Maliev.ServiceDefaults': 'c40a7f82cea347b949444dcd7fb730f2b8dc3c0e',
                'Legacy.Maliev.CompatibilityContracts': '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'}
SEALS = {'reviewedCandidateManifestSha256': '066067909b8bdf93955f7cd7d6be7ca4923aa84bcb4380ffaf58aa7f2edce486',
         'reviewedCandidatePatchSha256': '3c89cdfafbee04dc54c334c51611568d69ee4bcebdea32951a8bcef57fe579db',
         'baselineObserverSha256': '2040be722b37e44819a50fe217a0ce21911a6c0c03c2a8a481cb78fa6bcf7c92'}
OWNER_TEST = 'Legacy.Maliev.EmployeeService.Tests/EmployeeSignatureOwnerBoundaryHttpTests.cs'

def need(condition, message):
    if not condition:
        raise ValueError(message)

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def accepted_helper():
    path = Path(__file__).with_name('sealed_source_capsule.py')
    need(path.is_file() and not path.is_symlink(), 'regular decoder required')
    raw = path.read_bytes()
    need(sha(raw) == MODULE_SHA256, 'decoder seal mismatch')
    spec = importlib.util.spec_from_loader('employee_accepted_capsule', loader=None)
    helper = importlib.util.module_from_spec(spec)
    helper.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), helper.__dict__)
    return helper

def validate_policy(policy, helper):
    keys = {'schemaVersion','repository','baseCommit','acceptedModuleSha256','nativeExecutionGranted',
            'dependencyCommits','counts','forecastCases','capsules',*SEALS}
    need(set(policy) == keys, 'exact policy schema required')
    need(type(policy['schemaVersion']) is int and policy['schemaVersion'] == 1,
         'policy schema version mismatch')
    need(policy['repository'] == REPOSITORY and policy['baseCommit'] == BASE,
         'Employee repository/base mismatch')
    need(policy['acceptedModuleSha256'] == MODULE_SHA256 and policy['nativeExecutionGranted'] is False,
         'source-only accepted decoder required')
    need(policy['dependencyCommits'] == DEPENDENCIES and policy['counts'] == COUNTS,
         'frozen dependency/count mismatch')
    need(policy['forecastCases'] == {'focused':12,'adapted':3,'full':371}, 'forecast mismatch')
    need(all(policy[key] == value for key,value in SEALS.items()), 'reviewed source seal mismatch')
    need(set(policy['capsules']) == set(COUNTS), 'exact three capsules required')
    graph = {}; aliases = set()
    for role,count in COUNTS.items():
        bound = policy['capsules'][role]
        need(set(bound) == {'oid','sha256','bytes','rows'} and
             re.fullmatch('[a-f0-9]{40}',bound['oid']) and
             re.fullmatch('[a-f0-9]{64}',bound['sha256']) and
             type(bound['bytes']) is int and 0 < bound['bytes'] <= helper.MAX_ARCHIVE_BYTES,
             'bound capsule identity required')
        need(isinstance(bound['rows'],list) and len(bound['rows']) == count, 'capsule row count mismatch')
        for row in bound['rows']:
            name = helper.canonical_path(row['path'])
            need(name.startswith(role+'/') and name not in graph and name.casefold() not in aliases,
                 'cross-capsule graph recipe mismatch')
            graph[name] = row
            aliases.add(name.casefold())
    need(not any('/'.join(name.casefold().split('/')[:i]) in aliases
         for name in graph for i in range(1,len(name.split('/')))), 'file/parent collision')
    need(graph['baseline/'+OWNER_TEST]['sha256'] == SEALS['baselineObserverSha256'], 'observer drift')
    need(graph['candidate/'+OWNER_TEST]['sha256'] ==
         '8422e1fa9ece44f91e84f81affed6395d7605a551c7fea39fd29d7c7f7dbaa83', 'candidate test drift')
    return graph

def load_policy(raw, helper):
    need(sha(raw) == POLICY_SHA256, 'policy bootstrap seal mismatch')
    policy = helper.parse_json(raw)
    validate_policy(policy, helper)
    return policy

def validate_inputs(policy_raw, capsules, helper=None):
    helper = helper or accepted_helper()
    policy = load_policy(policy_raw, helper)
    need(set(capsules) == set(COUNTS), 'exact capsule inputs required')
    files = {}
    for role in COUNTS:
        raw = capsules[role]; bound = policy['capsules'][role]
        need(hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest() == bound['oid'],
             'Git blob OID mismatch')
        decoded = helper.validate_zip(raw,bound['sha256'],bound['bytes'],bound['rows'])
        need(not set(files).intersection(decoded), 'duplicate raw source')
        files.update(decoded)
    return policy,files

def materialize(policy_raw, capsules, root, owner, helper=None):
    helper = helper or accepted_helper()
    policy,files = validate_inputs(policy_raw,capsules,helper)
    need(re.fullmatch('[0-9]+-[0-9]+',owner) is not None, 'exact job owner required')
    root = Path(root)
    need(root.is_absolute() and root.name == 'employee-owner-source' and root.parent.is_dir()
         and not root.exists(), 'fresh private source root required')
    helper.reject_links(root)
    root.mkdir(mode=0o700)
    helper.write_new(root,'.employee-source-owner',owner.encode())
    for path in sorted(files):
        helper.write_new(root,path,files[path])
    receipt = {'repository':REPOSITORY,'baseCommit':BASE,'policySha256':sha(policy_raw),
        'counts':COUNTS,'rawFiles':len(files),'forecastCases':policy['forecastCases'],
        'actualTestsExecuted':0,'nativeExecutionGranted':False,
        'status':'frozen-source-materialized-runtime-unqualified'}
    helper.write_new(root,'intake-receipt.json',(json.dumps(receipt,sort_keys=True)+'\n').encode())
    return receipt

def release(root, owner, helper=None):
    helper = helper or accepted_helper()
    root = Path(root)
    need(root.is_absolute() and root.name == 'employee-owner-source', 'exact source root required')
    helper.reject_links(root)
    if not root.exists():
        return {'sourceRootAbsent':True,'nativeStarts':0}
    marker = root/'.employee-source-owner'
    helper.reject_links(marker)
    need(marker.is_file() and marker.read_bytes() == owner.encode(), 'owned source marker mismatch')
    for path in root.rglob('*'):
        helper.reject_links(path)
    shutil.rmtree(root)
    need(not root.exists(), 'owned source release failed')
    return {'sourceRootAbsent':True,'nativeStarts':0}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode',choices=('validate','fetch-materialize','release'))
    args = parser.parse_args()
    helper = accepted_helper()
    scripts = Path(__file__).resolve().parent
    if args.mode == 'validate':
        raw = (scripts/'employee-source-policy.json').read_bytes()
        capsules = {role:(scripts/'employee-source-packet'/f'{role}.zip').read_bytes() for role in COUNTS}
        _,files = validate_inputs(raw,capsules,helper)
        print(json.dumps({'validatedRawFiles':len(files),'nativeExecutionGranted':False}))
        return
    parent = Path(os.environ['RUNNER_TEMP'])
    need(parent.is_absolute() and parent.is_dir(), 'existing absolute runner temp required')
    root = parent/'employee-owner-source'; owner = os.environ['EMPLOYEE_SOURCE_OWNER']
    if args.mode == 'release':
        print(json.dumps(release(root,owner,helper)))
        return
    raw = (scripts/'employee-source-policy.json').read_bytes()
    policy = load_policy(raw,helper)
    capsules = {role:helper.fetch_git_blob(REPOSITORY,policy['capsules'][role]['oid']) for role in COUNTS}
    print(json.dumps(materialize(raw,capsules,root,owner,helper)))

if __name__ == '__main__':
    main()

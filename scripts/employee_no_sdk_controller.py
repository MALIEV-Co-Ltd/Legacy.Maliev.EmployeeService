"""Finite no-SDK caller connection; no policy signer, SDK or provider authority."""
import argparse
import hashlib
import os
from pathlib import Path
import re
import sys
import stat
import types


class QualificationRefused(ValueError):
    pass


CUSTODY_SOURCE_SHA256 = '8c3e7d9f4e17a0e4ede890b64c039f106c82d1d5be10e86a1bd607c3b4c47e5b'


def read_regular(path,limit=65536):
    path=Path(path)
    for node in (path,*path.parents):
        if node.is_symlink():raise QualificationRefused('Linked original input refused')
    fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_CLOEXEC|os.O_NONBLOCK)
    try:
        before=os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_nlink!=1 or not 0<before.st_size<=limit:
            raise QualificationRefused('Bounded single-link regular original input required')
        raw=b''
        while len(raw)<=limit:
            chunk=os.read(fd,limit+1-len(raw))
            if not chunk:break
            raw+=chunk
        after=os.fstat(fd)
        fields=('st_dev','st_ino','st_mode','st_uid','st_gid','st_nlink','st_size','st_mtime_ns','st_ctime_ns')
        if len(raw)!=before.st_size or len(raw)>limit or any(getattr(before,key)!=getattr(after,key) for key in fields):
            raise QualificationRefused('Original input changed during bounded read')
        return raw
    finally:os.close(fd)


def load_no_sdk_custody(scripts):
    path=Path(scripts)/'employee_linux_custody.py'
    for node in (path,*path.parents):
        if node.is_symlink():raise QualificationRefused('Linked custody source refused')
    # Linux runtime uses nonblocking no-follow regular-file reads. Portable
    # source tests may inspect exact checked-out bytes without admission.
    if sys.platform=='linux':raw=read_regular(path)
    else:
        with path.open('rb') as stream:raw=stream.read(65537)
    if not raw or len(raw)>65536 or hashlib.sha256(raw).hexdigest()!=CUSTODY_SOURCE_SHA256:
        raise QualificationRefused('Reviewed custody source bytes differ')
    module=types.ModuleType('sealed_employee_no_sdk_custody')
    module.__file__=str(path.resolve())
    exec(compile(raw,str(path),'exec'),module.__dict__)
    return module


def qualify_no_sdk(scripts, policy_path, policy_sha, evidence, evidence_fd):
    """Connect original finite policy to the retained-FD qualifier in-process.

    The caller must supply the independently reviewed policy pin and retained
    root-owned descriptor. This does not supply either, or grant SDK authority.
    """
    if sys.platform!='linux' or not hasattr(os,'geteuid') or os.geteuid()!=0 or not hasattr(os,'pidfd_open'):
        raise QualificationRefused('Actual Linux root pidfd caller required')
    if type(evidence_fd) is not int or evidence_fd<0:
        raise QualificationRefused('Caller-retained evidence descriptor required')
    if type(policy_sha) is not str or not re.fullmatch('[0-9a-f]{64}',policy_sha):
        raise QualificationRefused('Independent original policy pin required')
    if not isinstance(evidence,Path) or not evidence.is_absolute():
        raise QualificationRefused('Absolute evidence label required')
    custody=load_no_sdk_custody(scripts)
    raw=read_regular(policy_path)
    if hashlib.sha256(raw).hexdigest()!=policy_sha:
        raise QualificationRefused('Original reviewed policy bytes differ')
    policy=custody.parse(raw)
    if (os.environ.get('GITHUB_ACTIONS')!='true' or
        os.environ.get('GITHUB_REPOSITORY')!='MALIEV-Co-Ltd/Legacy.Maliev.EmployeeService' or
        os.environ.get('GITHUB_SHA')!=policy.get('sourceHead') or
        os.environ.get('GITHUB_RUN_ID')!=policy.get('runId') or
        os.environ.get('GITHUB_RUN_ATTEMPT')!=str(policy.get('attempt'))):
        raise QualificationRefused('Original policy differs from live hosted allocation')
    observer=custody.ProcObserver()
    custody.qualification_policy(raw,policy_sha,observer.boot(),CUSTODY_SOURCE_SHA256,custody.now())
    # Qualifier rechecks the exact pinned policy, actual boot/source, fresh
    # physical floor/census and retained directory owner before any acquisition.
    return custody.qualify_no_sdk(types.SimpleNamespace(
        policy=str(policy_path),policy_sha=policy_sha,evidence=str(evidence),
        evidence_fd=evidence_fd,adapter=str(Path(scripts)/'employee_linux_sdk_owner.py')))


def main(argv=None):
    parser=argparse.ArgumentParser()
    parser.add_argument('--policy',type=Path,required=True)
    parser.add_argument('--policy-sha',required=True)
    parser.add_argument('--evidence',type=Path,required=True)
    parser.add_argument('--evidence-fd',type=int,required=True)
    args=parser.parse_args(argv)
    return qualify_no_sdk(Path(__file__).resolve().parent,args.policy,args.policy_sha,args.evidence,args.evidence_fd)


if __name__=='__main__':main()

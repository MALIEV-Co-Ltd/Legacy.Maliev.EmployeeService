"""Detached authority verification source. No signing, enrollment or execution API.

Production Root trust remains absent. Public RFC test vectors are not enrolled
Root authority. Authentication verifies an attestation, never actual runtime truth.
"""
import datetime as dt
import hashlib
import json
import re

TRUSTED_ROOT_PUBLIC_KEY_SHA256 = None
REPO = 'MALIEV-Co-Ltd/Legacy.Maliev.EmployeeService'
SOURCE_POLICY = '88507d27eeee6b7b4095bb556a5c7d1fc2d1b859786d0d5ba891c5bada0be895'
PHASE_CONTRACT = 'a0100ee0a6c54f9f561e0c57e06b1aa49f5f9ed32d45b381347cea642ee0745c'
HEX = re.compile(r'[0-9a-f]{64}\Z')
BOOT = re.compile(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z')
DOMAIN = b'employee-detached-authority/v1\x00'
KINDS = {'sdk-policy','baseline-review','stage-grant','backend-qualification'}
POLICY_FIELDS = {'stageSha256','grantSha256','backendReceiptSha256','backendModuleSha256',
                 'phaseContractSha256','sourcePolicySha256','authoritySchemaIntegrationReviewed',
                 'actualBaselineReviewSha256'}
REVIEW_FIELDS = {'schemaVersion','repository','sourceHead','runId','attempt','bootId',
                 'reviewedUtc','expiresUtc','sourcePolicySha256','phaseContractSha256',
                 'baselineTrxSha256','baselineBuildLogSha256','custodyReceiptSha256',
                 'caseMapSha256','baselineCases','compatibilityAuthPassed',
                 'runtimeProvenanceQualified','decision','owner18Reassignment'}

def need(ok,message):
    if not ok:raise ValueError(message)

def closed(value,fields):
    need(type(value) is dict and set(value)==fields,'closed authority schema required')

def encode(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=True,allow_nan=False).encode('ascii')

def digest(raw):
    need(type(raw) is bytes and 0<len(raw)<=16384,'bounded original authority bytes required')
    return hashlib.sha256(raw).hexdigest()

def parse(raw):
    digest(raw)
    depth=0;quoted=False;escape=False
    for byte in raw:
        if quoted:
            if escape:escape=False
            elif byte==92:escape=True
            elif byte==34:quoted=False
        elif byte==34:quoted=True
        elif byte in (91,123):
            depth+=1;need(depth<=16,'authority JSON depth exceeded')
        elif byte in (93,125):depth-=1
    def pairs(items):
        out={}
        for key,value in items:
            need(key not in out,'duplicate authority key');out[key]=value
        return out
    value=json.loads(raw,object_pairs_hook=pairs,
                     parse_constant=lambda _: (_ for _ in ()).throw(ValueError('nonfinite authority')))
    need(encode(value)==raw,'original authority must use canonical exact bytes')
    return value

def utc(value):
    need(type(value) is str and value.endswith('Z'),'explicit UTC required')
    value=dt.datetime.fromisoformat(value[:-1]+'+00:00')
    need(value.utcoffset()==dt.timedelta(0),'UTC required');return value

def time_window(start,end,now):
    need(type(now) is dt.datetime and now.tzinfo is not None and now.utcoffset()==dt.timedelta(0),'UTC observation clock required')
    first,last=utc(start),utc(end)
    need(first<=now<last and 0<(last-first).total_seconds()<=3600,'expired or excessive original authority')

def signing_bytes(kind,raw):
    need(kind in KINDS,'unknown authority domain');digest(raw)
    return DOMAIN+kind.encode('ascii')+b'\x00'+raw

class DetachedVerifier:
    def __init__(self,public_key_raw,trusted_public_key_sha256):
        need(type(public_key_raw) is bytes and len(public_key_raw)==32,'raw Ed25519 public key required')
        need(type(trusted_public_key_sha256) is str and HEX.fullmatch(trusted_public_key_sha256),
             'independently enrolled public key pin absent')
        need(hashlib.sha256(public_key_raw).hexdigest()==trusted_public_key_sha256,'trusted Root key differs')
        self.key_raw=public_key_raw;self.key_sha256=trusted_public_key_sha256

    def authenticate(self,kind,raw,signature):
        payload=parse(raw)
        need(type(signature) is bytes and len(signature)==64,'detached Ed25519 signature required')
        # No fallback algorithm, key generation, URL fetch, shell or signing API.
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
        from cryptography.exceptions import InvalidSignature
        try:Ed25519PublicKey.from_public_bytes(self.key_raw).verify(signature,signing_bytes(kind,raw))
        except InvalidSignature as error:raise ValueError('original authority signature invalid') from error
        return payload

    def sdk_policy_callback(self,signature,stage,clock):
        """Callback has existing SDK execute_qualification(raw,key_sha) signature."""
        original_stage=parse(encode(stage))
        def verify(raw,key_sha):
            need(key_sha==self.key_sha256,'SDK verifier trusted key mismatch')
            policy=self.authenticate('sdk-policy',raw,signature);closed(policy,POLICY_FIELDS)
            time_window(original_stage['startsUtc'],original_stage['expiresUtc'],clock())
            need(policy['stageSha256']==hashlib.sha256(encode(original_stage)).hexdigest(),'exact original stage differs')
            need(policy['sourcePolicySha256']==SOURCE_POLICY==original_stage['sourcePolicySha256'] and
                 policy['phaseContractSha256']==PHASE_CONTRACT==original_stage['phaseContractSha256'],
                 'frozen original source binding differs')
            for field in ('grantSha256','backendReceiptSha256','backendModuleSha256'):
                need(type(policy[field]) is str and HEX.fullmatch(policy[field]),'sealed original authority inputs required')
            need(policy['grantSha256']==original_stage['rootGrantSha256'] and
                 policy['backendReceiptSha256']==original_stage['backendQualificationSha256'],
                 'original grant or qualification receipt binding differs')
            need(policy['authoritySchemaIntegrationReviewed'] is True,'authority schema adaptation has not been attested')
            phase=original_stage['phaseId']
            need(type(phase) is str and re.fullmatch(r'(?:baseline|candidate)-[a-z]{1,20}',phase),'exact phase required')
            review=policy['actualBaselineReviewSha256']
            need((phase.startswith('candidate-') and type(review) is str and HEX.fullmatch(review)) or
                 (phase.startswith('baseline-') and review is None),'candidate review cannot be waived or invented for baseline')
            return True
        return verify

    def baseline_review_callback(self,signature,context,clock):
        closed(context,{'sourceHead','runId','attempt','bootId'})
        need(type(context['sourceHead']) is str and re.fullmatch(r'[0-9a-f]{40}',context['sourceHead']),'exact source head required')
        need(type(context['runId']) is str and re.fullmatch(r'[1-9][0-9]{0,19}',context['runId']) and
             type(context['attempt']) is int and 0<context['attempt']<=1000 and
             type(context['bootId']) is str and BOOT.fullmatch(context['bootId']),'actual hosted generation context required')
        expected=dict(context)
        def verify(raw,expected_raw_sha256):
            need(type(expected_raw_sha256) is str and HEX.fullmatch(expected_raw_sha256) and
                 digest(raw)==expected_raw_sha256,'actual review receipt bytes differ')
            review=self.authenticate('baseline-review',raw,signature);closed(review,REVIEW_FIELDS)
            need(type(review['schemaVersion']) is int and review['schemaVersion']==1 and review['repository']==REPO,'foreign review schema')
            need(all(type(review[key]) is type(expected[key]) and review[key]==expected[key] for key in expected),'review belongs to another hosted generation')
            time_window(review['reviewedUtc'],review['expiresUtc'],clock())
            need(review['sourcePolicySha256']==SOURCE_POLICY and review['phaseContractSha256']==PHASE_CONTRACT and
                 review['caseMapSha256']=='91e9181ed1953c6c657ce93ed61dfede60a1109d8e2f785003ca21d13788d6e9','frozen review source differs')
            for field in ('baselineTrxSha256','baselineBuildLogSha256','custodyReceiptSha256'):
                need(type(review[field]) is str and HEX.fullmatch(review[field]),'actual raw artifact seals required')
            need(type(review['baselineCases']) is int and review['baselineCases']==12 and
                 type(review['compatibilityAuthPassed']) is int and review['compatibilityAuthPassed']==6 and
                 review['runtimeProvenanceQualified'] is True and review['decision']=='runtime-owner-reassignment-red',
                 'actual independently qualified baseline review required')
            witness=review['owner18Reassignment']
            exact={'routeOwner':17,'bodyOwner':18,'selectedId':101,'httpStatus':204,'observedOwner':18,'graphUnchanged':False}
            closed(witness,set(exact))
            need(all(type(witness[key]) is type(value) and witness[key]==value for key,value in exact.items()),'owner18 runtime witness differs')
            return True
        return verify

def production_verifier(public_key_raw):
    # No source/control result enrolls a key. The existing SDK sentinel also
    # remains None; integration into an admitted runner is separately required.
    return DetachedVerifier(public_key_raw,TRUSTED_ROOT_PUBLIC_KEY_SHA256)

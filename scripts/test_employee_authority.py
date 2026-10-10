"""Real signature checks over synthetic attestations using PUBLIC RFC8032 vector1.

The published vector seed is test data, never an enrolled Root key or live grant.
"""
import copy
import datetime as dt
import hashlib
import unittest
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives import serialization
import employee_authority as a

PUBLIC_RFC_SEED=bytes.fromhex('9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60')
NOW=dt.datetime(2026,10,8,10,tzinfo=dt.timezone.utc)

class Controls(unittest.TestCase):
    def setUp(self):
        self.test_signer=Ed25519PrivateKey.from_private_bytes(PUBLIC_RFC_SEED)
        self.public=self.test_signer.public_key().public_bytes(serialization.Encoding.Raw,serialization.PublicFormat.Raw)
        self.pin=hashlib.sha256(self.public).hexdigest();self.verifier=a.DetachedVerifier(self.public,self.pin)
        self.stage={'startsUtc':'2026-10-08T09:59:00Z','expiresUtc':'2026-10-08T10:10:00Z',
                    'sourcePolicySha256':a.SOURCE_POLICY,'phaseContractSha256':a.PHASE_CONTRACT,
                    'phaseId':'baseline-restore','rootGrantSha256':'1'*64,'backendQualificationSha256':'2'*64}
        self.policy={'stageSha256':hashlib.sha256(a.encode(self.stage)).hexdigest(),'grantSha256':'1'*64,
                     'backendReceiptSha256':'2'*64,'backendModuleSha256':'3'*64,
                     'phaseContractSha256':a.PHASE_CONTRACT,'sourcePolicySha256':a.SOURCE_POLICY,
                     'authoritySchemaIntegrationReviewed':True,'actualBaselineReviewSha256':None}
        self.context={'sourceHead':'4'*40,'runId':'123','attempt':1,'bootId':'11111111-2222-3333-4444-555555555555'}
        self.review={'schemaVersion':1,'repository':a.REPO,**self.context,
                     'reviewedUtc':'2026-10-08T09:59:00Z','expiresUtc':'2026-10-08T10:10:00Z',
                     'sourcePolicySha256':a.SOURCE_POLICY,'phaseContractSha256':a.PHASE_CONTRACT,
                     'baselineTrxSha256':'5'*64,'baselineBuildLogSha256':'6'*64,'custodyReceiptSha256':'7'*64,
                     'caseMapSha256':'91e9181ed1953c6c657ce93ed61dfede60a1109d8e2f785003ca21d13788d6e9',
                     'baselineCases':12,'compatibilityAuthPassed':6,'runtimeProvenanceQualified':True,
                     'decision':'runtime-owner-reassignment-red','owner18Reassignment':
                     {'routeOwner':17,'bodyOwner':18,'selectedId':101,'httpStatus':204,'observedOwner':18,'graphUnchanged':False}}
    def signed(self,kind,value):
        raw=a.encode(value);return raw,self.test_signer.sign(a.signing_bytes(kind,raw))
    def policy_call(self,policy=None,stage=None,now=NOW):
        raw,sig=self.signed('sdk-policy',self.policy if policy is None else policy)
        return self.verifier.sdk_policy_callback(sig,self.stage if stage is None else stage,lambda:now)(raw,self.pin)
    def review_call(self,review=None,context=None,now=NOW):
        raw,sig=self.signed('baseline-review',self.review if review is None else review)
        return self.verifier.baseline_review_callback(sig,self.context if context is None else context,lambda:now)(raw,hashlib.sha256(raw).hexdigest())
    def test_published_rfc_empty_message_vector(self):
        expected=bytes.fromhex('e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b')
        self.assertEqual(self.test_signer.sign(b''),expected)
    def test_valid_original_policy(self):self.assertTrue(self.policy_call())
    def test_valid_synthetic_review_authentication(self):self.assertTrue(self.review_call())
    def test_production_key_stays_unenrolled(self):
        with self.assertRaises(ValueError):a.production_verifier(self.public)
    def test_wrong_key_pin(self):
        with self.assertRaises(ValueError):a.DetachedVerifier(self.public,'0'*64)
    def test_wrong_signature(self):
        raw,sig=self.signed('sdk-policy',self.policy)
        with self.assertRaises(ValueError):self.verifier.authenticate('sdk-policy',raw,bytes(64))
    def test_wrong_domain(self):
        raw,sig=self.signed('sdk-policy',self.policy)
        with self.assertRaises(ValueError):self.verifier.authenticate('baseline-review',raw,sig)
    def test_modified_raw_payload(self):
        raw,sig=self.signed('sdk-policy',self.policy)
        changed=raw.replace(b'backendModuleSha256',b'backendModuleSha257')
        self.assertNotEqual(changed,raw)
        with self.assertRaises(ValueError):self.verifier.authenticate('sdk-policy',changed,sig)
        with self.assertRaises(ValueError):self.verifier.authenticate('sdk-policy',raw.replace(b'111111',b'000000'),sig)
    def test_noncanonical_original_json_refused(self):
        raw,sig=self.signed('sdk-policy',self.policy)
        with self.assertRaises(ValueError):self.verifier.authenticate('sdk-policy',raw+b'\n',sig)
    def test_duplicate_depth_nonfinite_and_bounds(self):
        for raw in (b'{"x":1,"x":2}',b'{"x":NaN}',b'['*17+b'0'+b']'*17,b' '*16385):
            with self.subTest(raw=raw[:30]),self.assertRaises(ValueError):a.parse(raw)
    def test_expired_future_and_naive_policy_clocks(self):
        for now in (NOW+dt.timedelta(hours=1),NOW-dt.timedelta(hours=1),NOW.replace(tzinfo=None)):
            with self.subTest(now=now),self.assertRaises(ValueError):self.policy_call(now=now)
    def test_changed_stage_refused(self):
        with self.assertRaises(ValueError):self.policy_call(stage=self.stage|{'phaseId':'baseline-build'})
    def test_missing_review_candidate_refused(self):
        stage=self.stage|{'phaseId':'candidate-build'}
        policy=self.policy|{'stageSha256':hashlib.sha256(a.encode(stage)).hexdigest()}
        with self.assertRaises(ValueError):self.policy_call(policy,stage)
    def test_signed_candidate_review_pin_required(self):
        stage=self.stage|{'phaseId':'candidate-build'}
        policy=self.policy|{'stageSha256':hashlib.sha256(a.encode(stage)).hexdigest(),'actualBaselineReviewSha256':'8'*64}
        self.assertTrue(self.policy_call(policy,stage))
    def test_baseline_does_not_invent_candidate_review(self):
        with self.assertRaises(ValueError):self.policy_call(self.policy|{'actualBaselineReviewSha256':'8'*64})
    def test_missing_schema_adaptation_attestation(self):
        with self.assertRaises(ValueError):self.policy_call(self.policy|{'authoritySchemaIntegrationReviewed':False})
    def test_foreign_receipt_binding(self):
        with self.assertRaises(ValueError):self.policy_call(self.policy|{'backendReceiptSha256':'9'*64})
    def test_review_context_generation(self):
        for key,value in (('runId','124'),('attempt',2),('bootId','aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee')):
            with self.subTest(key=key),self.assertRaises(ValueError):self.review_call(self.review|{key:value})
    def test_review_context_bool_attempt(self):
        with self.assertRaises(ValueError):self.review_call(context=self.context|{'attempt':True})
    def test_review_requires_provenance_attestation(self):
        with self.assertRaises(ValueError):self.review_call(self.review|{'runtimeProvenanceQualified':False})
    def test_setup_failure_never_reviewed_red(self):
        with self.assertRaises(ValueError):self.review_call(self.review|{'decision':'setup-failure'})
    def test_duplicate_500_not_owner18_witness(self):
        value=copy.deepcopy(self.review);value['owner18Reassignment'].update(httpStatus=500,observedOwner=17,graphUnchanged=True)
        with self.assertRaises(ValueError):self.review_call(value)
    def test_nonempty_raw_evidence_seals(self):
        with self.assertRaises(ValueError):self.review_call(self.review|{'baselineTrxSha256':None})
    def test_review_counts_and_witness_types(self):
        for field,value in (('baselineCases',11),('compatibilityAuthPassed',True),('schemaVersion',True)):
            with self.subTest(field=field),self.assertRaises(ValueError):self.review_call(self.review|{field:value})
        data=copy.deepcopy(self.review);data['owner18Reassignment']['httpStatus']='204'
        with self.assertRaises(ValueError):self.review_call(data)
    def test_unknown_fields_and_signature_lengths(self):
        with self.assertRaises(ValueError):self.policy_call(self.policy|{'unexpected':True})
        raw,sig=self.signed('sdk-policy',self.policy)
        for signature in (sig[:63],sig+b'0'):
            with self.subTest(length=len(signature)),self.assertRaises(ValueError):self.verifier.authenticate('sdk-policy',raw,signature)

if __name__=='__main__':unittest.main()

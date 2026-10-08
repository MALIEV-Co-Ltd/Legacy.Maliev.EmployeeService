# Descriptor-owned receipt source slice

`OwnedReceipts` duplicates a caller-owned private directory descriptor. Receipt writes and reads resolve relative to that retained descriptor, so renaming its directory or replacing the former pathname cannot redirect custody to another directory. The caller retains ownership of its original descriptor; closing the store closes only the duplicate.

Writes are exclusive, canonical bounded JSON with nonfinite numbers refused. File metadata and exact length are rechecked after file fsync before any write acknowledgment. Files and their parent directory are fsynced. Reads refuse links, nonregular files, nonprivate modes, changed owner or unstable bounded content. A failed durable write may leave an exclusive partial receipt; it is retained as uncertainty and must not be silently overwritten or treated as a successful receipt.

The workflow runs bounded Python source controls on Ubuntu 24.04. Actual Linux filesystem controls cover renamed ancestry, foreign pathname replacement, symbolic and hard links, FIFO refusal, private modes, byte bounds and descriptor ownership. Portable syscall controls supplement those checks. Every temporary directory and descriptor is released in test cleanup; failures remain in the retained unittest transcript.

Acceptance requires all 18 controls executed without skips (8 portable syscall controls and 10 Linux filesystem controls), unchanged process descriptor identities and absence of every test-owned temporary directory. Count and cleanup evidence are retained separately. A timeout or missing evidence is not a pass or cleanup proof. Current Windows validation covers only the 8 portable controls; the 10 Linux checks remain unrun until admitted Linux source validation.

This slice has no Root policy, SDK, Docker, provider, network listener or systemd unit. Linux filesystem checks do not qualify Root evidence ownership transitions or an actual SDK/provider lifecycle. No native business tests or runtime acceptance are implied. The workflow exposes no dispatch input or execution grant.


## Custody consumption

The retained `employee_linux_custody.py` no-SDK route now requires an actual
caller-owned `--evidence-fd`. It duplicates that descriptor before policy or
manager access, routes every qualification receipt and manager-helper receipt
through the descriptor store, and closes only its own duplicate in `finally`.
The evidence pathname is a diagnostic label; it cannot redirect these writes.
Path-only calls fail before reading policy or launching helpers.

The receipt module is loaded lazily from exact pinned bytes, preserving the
existing isolated Python payload (`-I`). The SDK adapter remains byte-for-byte
sealed. Its separate cross-process watchdog and authenticated SDK/provider
connection remain unqualified; no runtime authority or retirement is inferred.
The no-SDK route still needs the reviewed finite policy, current Root slot,
fresh complete process census, physical 4 GiB floor, and actual Linux/systemd
capability observations. This workflow runs only source and filesystem controls;
it does not invoke that route or launch systemd, .NET, Go or containers.

The existing five-minute Linux workflow now requires 116 tests with zero skips:
18 receipt controls, all 87 retained custody controls, and eleven integration
regressions. Six new controls use actual Linux descriptors, directory replacement,
exclusive actor receipts, link refusal and descriptor cleanup. Manager results in
the remaining controls are synthetic. These results cannot replace the frozen
12 baseline, 15 focused candidate or 371 full candidate acceptance cases.

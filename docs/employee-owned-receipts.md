# Descriptor-owned receipt source slice

`OwnedReceipts` duplicates a caller-owned private directory descriptor. Receipt writes and reads resolve relative to that retained descriptor, so renaming its directory or replacing the former pathname cannot redirect custody to another directory. The caller retains ownership of its original descriptor; closing the store closes only the duplicate.

Writes are exclusive, canonical bounded JSON with nonfinite numbers refused. File metadata and exact length are rechecked after file fsync before any write acknowledgment. Files and their parent directory are fsynced. Reads refuse links, nonregular files, nonprivate modes, changed owner or unstable bounded content. A failed durable write may leave an exclusive partial receipt; it is retained as uncertainty and must not be silently overwritten or treated as a successful receipt.

The workflow runs bounded Python source controls on Ubuntu 24.04. Actual Linux filesystem controls cover renamed ancestry, foreign pathname replacement, symbolic and hard links, FIFO refusal, private modes, byte bounds and descriptor ownership. Portable syscall controls supplement those checks. Every temporary directory and descriptor is released in test cleanup; failures remain in the retained unittest transcript.

Acceptance requires all 18 controls executed without skips (8 portable syscall controls and 10 Linux filesystem controls), unchanged process descriptor identities and absence of every test-owned temporary directory. Count and cleanup evidence are retained separately. A timeout or missing evidence is not a pass or cleanup proof. Current Windows validation covers only the 8 portable controls; the 10 Linux checks remain unrun until admitted Linux source validation.

This slice has no Root policy, SDK, Docker, provider, network listener or systemd unit. Linux filesystem checks do not qualify Root evidence ownership transitions or an actual SDK/provider lifecycle. No native business tests or runtime acceptance are implied. The workflow exposes no dispatch input or execution grant.

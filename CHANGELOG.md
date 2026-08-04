# Changelog
All notable changes to this project will be documented in this file. See [conventional commits](https://www.conventionalcommits.org/) for commit guidelines.

- - -
## [v0.1.0](https://github.com/barryw/SixtyFiveXX/compare/44efb8558c53c06fb2a8ae987a8c552cff4fd0e9..v0.1.0) - 2026-08-04
#### Features
- add a disassembler driven by the engine's opcode table - ([738c0b8](https://github.com/barryw/SixtyFiveXX/commit/738c0b88d8f7d56b7bc0813173f52089c77ceb1d)) - Barry Walker
- model the 6510's floating port bits - ([b538f20](https://github.com/barryw/SixtyFiveXX/commit/b538f203e3270b7da942209dd9b82c09da9c6dd6)) - Barry Walker, Claude Opus 5 (1M context)
- intercept the 6510's $00 and $01 before the bus - ([85e29e7](https://github.com/barryw/SixtyFiveXX/commit/85e29e7d591b37c97e83e5698fe972f4503c0a66)) - Barry Walker, Claude Opus 5 (1M context)
- add the 6510 variant, inheriting the 6502 exactly - ([027636a](https://github.com/barryw/SixtyFiveXX/commit/027636a3b4361bc084886e0e619077a51174f549)) - Barry Walker, Claude Opus 5 (1M context)
- certify the WDC 65C02 - ([bee6b3c](https://github.com/barryw/SixtyFiveXX/commit/bee6b3c846cbceb71a7831c932a7f18b0683a6af)) - Barry Walker, Claude Opus 5 (1M context)
- certify the Rockwell 65C02 - ([3ee1d5b](https://github.com/barryw/SixtyFiveXX/commit/3ee1d5ba47510830bae59008a35969e929b969a7)) - Barry Walker, Claude Opus 5 (1M context)
- certify the Synertek 65C02 - ([a995a3b](https://github.com/barryw/SixtyFiveXX/commit/a995a3b82ca1783eb5c0c342d84716f5cb9ecf76)) - Barry Walker, Claude Opus 5 (1M context)
- add CMOS decimal-mode arithmetic - ([049819c](https://github.com/barryw/SixtyFiveXX/commit/049819cb29890bc3a6ae238ffe32e195cea3e93d)) - Barry Walker, Claude Opus 5 (1M context)
- add the CMOS indexing and read-modify-write deltas - ([1b13798](https://github.com/barryw/SixtyFiveXX/commit/1b1379843869fa91f36763b7cc72b81e2c305da4)) - Barry Walker, Claude Opus 5 (1M context)
- add the CMOS addressing modes and operations - ([fdf3fa9](https://github.com/barryw/SixtyFiveXX/commit/fdf3fa9073332b574d573f650e185a0e0eb6bfaf)) - Barry Walker, Claude Opus 5 (1M context)
- select the interrupt sequence per variant - ([76b6249](https://github.com/barryw/SixtyFiveXX/commit/76b6249ec9153bf0b359e10f51c3acead91d3a0c)) - Barry Walker, Claude Opus 5 (1M context)
- add the RDY halt line and the SO pin - ([4f5eaa5](https://github.com/barryw/SixtyFiveXX/commit/4f5eaa555ca5e082628b7571d61ca8cdddde6e44)) - Barry Walker
- add NMI hijacking of an in-progress BRK or IRQ sequence - ([c5666ca](https://github.com/barryw/SixtyFiveXX/commit/c5666ca7e2e84113d7f6ad0ae72c9a6a8ad0f733)) - Barry Walker
- add the NMI pin with edge latching and priority over IRQ - ([9f075a7](https://github.com/barryw/SixtyFiveXX/commit/9f075a7005c4a0f70e9d95ec5a1b91f24b1e13a1)) - Barry Walker
- add the IRQ pin with hardware-correct interrupt polling - ([7657a9c](https://github.com/barryw/SixtyFiveXX/commit/7657a9c0def18df9e8cb38e4f4b0ee5c2dfa9dc9)) - Barry Walker
- add the twelve JAM opcodes and the IsJammed state - ([00206d5](https://github.com/barryw/SixtyFiveXX/commit/00206d57067d6fb41b087072ada0ea588d2bee07)) - Barry Walker
- add the unstable ANE, LXA, LAS, SHA, SHX, SHY and TAS opcodes - ([3d1f610](https://github.com/barryw/SixtyFiveXX/commit/3d1f6108abc4fffb18e3d2062a87efeb5a895f55)) - Barry Walker
- add ANC, ALR, ARR, SBX and the duplicate SBC opcode - ([52d2728](https://github.com/barryw/SixtyFiveXX/commit/52d27280350b6b61d904815f02104845c4490124)) - Barry Walker
- add LAX and SAX opcodes - ([a2af608](https://github.com/barryw/SixtyFiveXX/commit/a2af60876ac2cb1bbfa0dd27937cfd537bb836bb)) - Barry Walker
- add SLO, RLA, SRE, RRA, DCP and ISC combination opcodes - ([e16dbdb](https://github.com/barryw/SixtyFiveXX/commit/e16dbdbb8ee9eff39b86149f582bab23c806c0ef)) - Barry Walker
- add the 27 undocumented multi-byte NOP opcodes - ([24928ce](https://github.com/barryw/SixtyFiveXX/commit/24928ced6c9195b84b399a42e59f5e61f643215d)) - Barry Walker, Claude Opus 5 (1M context)
- add throughput benchmark, performance gate, CI pipeline and README - ([73eb154](https://github.com/barryw/SixtyFiveXX/commit/73eb15421be7805d07ca5472e9a015f7cb503e05)) - Barry Walker
- add cycle-accurate reset and the Step, Run and RunUntil API - ([a6b83d7](https://github.com/barryw/SixtyFiveXX/commit/a6b83d7acd1338ae67bb2804d1713d068b267c5d)) - Barry Walker
- add ALU with binary and NMOS decimal ADC and SBC - ([279d0b7](https://github.com/barryw/SixtyFiveXX/commit/279d0b7606800ce3641a53b6da1a3647f95aaf36)) - Barry Walker
- add stack instructions, JSR, RTS, RTI, BRK, and JMP - ([4b14035](https://github.com/barryw/SixtyFiveXX/commit/4b1403563a6375324445d98c43dc6678531365af)) - Barry Walker
- add branches with taken and page-cross timing - ([cc8ad77](https://github.com/barryw/SixtyFiveXX/commit/cc8ad77c0f6673d5fbb46f17c72b846468cc844b)) - Barry Walker
- add indexed indirect and indirect indexed addressing - ([08b492b](https://github.com/barryw/SixtyFiveXX/commit/08b492baa4132a15fa67f5dab03ea5b3b4797952)) - Barry Walker
- add indexed addressing with page-cross timing and dummy reads - ([9bc7d9a](https://github.com/barryw/SixtyFiveXX/commit/9bc7d9a0595158bf9a203b3c5fd6efaa8e51a848)) - Barry Walker
- add zero page and absolute addressing with NMOS RMW dummy write - ([8d4e3c0](https://github.com/barryw/SixtyFiveXX/commit/8d4e3c00354a6d478a7fbe8a7a41b06a94f9625b)) - Barry Walker
- add Cpu tick loop with implied and immediate addressing - ([ce1f459](https://github.com/barryw/SixtyFiveXX/commit/ce1f459700db13367dc3a62df8fa6fa9fe66d335)) - Barry Walker
- expand opcode descriptors into a flat micro-op table - ([d5c815e](https://github.com/barryw/SixtyFiveXX/commit/d5c815ed471e5dda460f75d389e3487c0b3dd317)) - Barry Walker
- add opcode descriptor model and 6502 legal opcode table - ([2abd1e7](https://github.com/barryw/SixtyFiveXX/commit/2abd1e7cd1ffcb26d4b15d85e0122aec8ab14672)) - Barry Walker
- add CpuState register struct and status flag constants - ([7c43e07](https://github.com/barryw/SixtyFiveXX/commit/7c43e0718504a14abcf3877c552a21efd19ff365)) - Barry Walker
- add solution scaffold and IBus with FlatBus and RefBus - ([da5ddb9](https://github.com/barryw/SixtyFiveXX/commit/da5ddb9b315b678357f0a2f218bb5c416747827e)) - Barry Walker
#### Bug Fixes
- stop the download caches racing each other - ([cbe26ad](https://github.com/barryw/SixtyFiveXX/commit/cbe26ad8d8b7860021a35137cc8eac65d8962fda)) - Barry Walker
- read only the bytes an instruction actually has - ([26a8938](https://github.com/barryw/SixtyFiveXX/commit/26a8938f8d144022799744249b5eecde9f8fc2d1)) - Barry Walker
- clear the 6510's port registers on RES - ([65a4802](https://github.com/barryw/SixtyFiveXX/commit/65a480227d75c4d4f9b36558a122e34763332af2)) - Barry Walker
- drive PC when RDY halts a WAI or STP hold - ([ca9ea48](https://github.com/barryw/SixtyFiveXX/commit/ca9ea48a8af7e964a09b4ee1e3a95bbd0265e8c7)) - Barry Walker, Claude Opus 5 (1M context)
- address the whole-branch review of the phase 3 refactor - ([b5035c5](https://github.com/barryw/SixtyFiveXX/commit/b5035c5ca8f517161d767cce21789400f4e3376c)) - Barry Walker, Claude Opus 5 (1M context)
- make Cpu<TBus, TVariant> public again via a public variant contract - ([f0b5773](https://github.com/barryw/SixtyFiveXX/commit/f0b57733804d6071ffd884979a8f65f0f09b45d0)) - Barry Walker, Claude Opus 5 (1M context)
- suppress chore from changelog, fix contributing doc order and gaps - ([1ad6310](https://github.com/barryw/SixtyFiveXX/commit/1ad6310ab5e4f8d9743307bc14b352ffc7eb1923)) - Barry Walker, Claude Opus 5 (1M context)
- refuse ambiguous version matches during verification - ([5aa4d79](https://github.com/barryw/SixtyFiveXX/commit/5aa4d79a0cc0b78222f5ed5214a2c47a7df11555)) - Barry Walker
- verify stamped elements after sed, don't trust silent no-op - ([1e328eb](https://github.com/barryw/SixtyFiveXX/commit/1e328ebdd0b93e8b8fbdeb66f2e0198ecd6d3ea0)) - Barry Walker
- Reset() discards a pending NMI, but not the NMI line level - ([33e7e15](https://github.com/barryw/SixtyFiveXX/commit/33e7e1562b92d6792c07fd7a8afbefc5e2e851f5)) - Barry Walker, Claude Opus 5 (1M context)
- commit the interrupt vector at T5 phase 1, not the vector read - ([53f5266](https://github.com/barryw/SixtyFiveXX/commit/53f526686fd03bc9d5e01816de3ca28bd27b1c5a)) - Barry Walker, Claude Opus 5 (1M context)
- address Task 5 Klaus interrupt port review findings - ([8d79344](https://github.com/barryw/SixtyFiveXX/commit/8d793444d7de74cb79291d70d1867a257f72f86f)) - Barry Walker, Claude Opus 5 (1M context)
- address Task 4 RDY/SO review findings - ([352e44d](https://github.com/barryw/SixtyFiveXX/commit/352e44d4fd84a6a4f49e5020020c432aecac731c)) - Barry Walker, Claude Opus 5 (1M context)
- restrict NMI hijack guard to IRQ-vectored sequences - ([eb5d2a4](https://github.com/barryw/SixtyFiveXX/commit/eb5d2a466cce6f593c91d74f95f35a252e900dcb)) - Barry Walker, Claude Opus 5 (1M context)
- correct the benchmark branch displacement in the Phase 1 plan - ([803dde7](https://github.com/barryw/SixtyFiveXX/commit/803dde7568be2dd88e6747c4792147c9fac42b8d)) - Barry Walker, Claude Opus 5 (1M context)
- correct benchmark branch offset and guard against workload derailment - ([456ae9c](https://github.com/barryw/SixtyFiveXX/commit/456ae9c544540010635a16068e6a33b606d4fe5b)) - Barry Walker
#### Documentation
- correct how the CI skip marker is matched - ([5389892](https://github.com/barryw/SixtyFiveXX/commit/538989245c3936af94c57ee7f2961c31b481f12b)) - Barry Walker
- document how a release is actually cut - ([5c5d484](https://github.com/barryw/SixtyFiveXX/commit/5c5d484db2430bf6a7cde6bb5e4fbb2dbd3498ae)) - Barry Walker
- add the phase 6b sim6502 adapter plan - ([937342d](https://github.com/barryw/SixtyFiveXX/commit/937342d003f93cc96658e4dcd6abe796a8c310f1)) - Barry Walker
- describe what the package actually contains - ([b4d8e4d](https://github.com/barryw/SixtyFiveXX/commit/b4d8e4dc6e6f571d93a07322be1fc51c28a70fd3)) - Barry Walker
- record the disassembler and fix a stale README example - ([dd2ac56](https://github.com/barryw/SixtyFiveXX/commit/dd2ac56fb454777878b13a2ac9e6ddde73666b69)) - Barry Walker
- add the phase 6a disassembler implementation plan - ([c267937](https://github.com/barryw/SixtyFiveXX/commit/c267937573d6e45797b132612f059930f3cf0ede)) - Barry Walker
- record what the 6510 gate does and does not certify - ([b04d259](https://github.com/barryw/SixtyFiveXX/commit/b04d2598dbbc86fadac742c325d3d94515f53886)) - Barry Walker
- add the phase 5 6510 implementation plan - ([63e1dd9](https://github.com/barryw/SixtyFiveXX/commit/63e1dd95f7a1a2d94c21ed0f5272bb14ddead121)) - Barry Walker, Claude Opus 5 (1M context)
- mark phase 4 complete and correct what the vectors disproved - ([4082424](https://github.com/barryw/SixtyFiveXX/commit/4082424c0da37b5a95ac7f0420e67ac5f6f9c2ee)) - Barry Walker, Claude Opus 5 (1M context)
- record the vector footprint and the offline path - ([7e44671](https://github.com/barryw/SixtyFiveXX/commit/7e446712945406a910469c04314ab9628110335e)) - Barry Walker, Claude Opus 5 (1M context)
- add the phase 4 65C02 implementation plan - ([fc27933](https://github.com/barryw/SixtyFiveXX/commit/fc279331657b549f8fbe269c39c2fe15ae99d363)) - Barry Walker, Claude Opus 5 (1M context)
- make the packed-assembly public-surface test a phase 3 gate - ([ee56053](https://github.com/barryw/SixtyFiveXX/commit/ee56053b00b5296888199e5ab01efc03943008a2)) - Barry Walker
- add the phase 3 variant refactor plan - ([df5bbcd](https://github.com/barryw/SixtyFiveXX/commit/df5bbcd8067357137b9f0006f285f55d9e56c27d)) - Barry Walker
- add variant-cores design and correct the 6510 gate - ([f7d0104](https://github.com/barryw/SixtyFiveXX/commit/f7d0104dfd641f7e1faf8b8f7aa58a5e09844ab8)) - Barry Walker, Claude Opus 5 (1M context)
- fix pre-merge review findings for the nuget.org release - ([b6a641b](https://github.com/barryw/SixtyFiveXX/commit/b6a641ba71d9e3025134585331aeb35372d38f05)) - Barry Walker, Claude Opus 5 (1M context)
- record test-project multi-targeting in the release plan - ([15ea56e](https://github.com/barryw/SixtyFiveXX/commit/15ea56e797a7eae80fe7830414fee1b13f25c840)) - Barry Walker
- harden the release plan's asset listing against pipefail - ([23514f4](https://github.com/barryw/SixtyFiveXX/commit/23514f4f8373b730e4b1cedd8cd909b847dc027a)) - Barry Walker
- fix the publish step's image in the release plan - ([683871e](https://github.com/barryw/SixtyFiveXX/commit/683871eafa897731ff157aae47d20e4955b420f3)) - Barry Walker
- add release engineering implementation plan - ([e1843bb](https://github.com/barryw/SixtyFiveXX/commit/e1843bb0996767c712f5617b762ac89cdef5d3dc)) - Barry Walker, Claude Opus 5 (1M context)
- rebase CI design onto the woodpecker-release house standard - ([7b96992](https://github.com/barryw/SixtyFiveXX/commit/7b96992c57a942e9be21c7010ee9e19d4b17a269)) - Barry Walker, Claude Opus 5 (1M context)
- revise CI design for cog versioning and GitHub releases - ([f4cbc74](https://github.com/barryw/SixtyFiveXX/commit/f4cbc743b7bbf9979a7289f6d96217ae6a5c1acc)) - Barry Walker, Claude Opus 5 (1M context)
- add CI and NuGet publishing design - ([8ec8dcf](https://github.com/barryw/SixtyFiveXX/commit/8ec8dcff3ea94ef323ba0c9d2e4ad73120df7a18)) - Barry Walker, Claude Opus 5 (1M context)
- fix stale hijack-window docs after the P-push relocation - ([6947845](https://github.com/barryw/SixtyFiveXX/commit/6947845799863bf27b4de5b3131519afe130965d)) - Barry Walker, Claude Opus 5 (1M context)
- apply final-review documentation fixes to phase2b - ([18cd54a](https://github.com/barryw/SixtyFiveXX/commit/18cd54aca3fadfaf9d317d48003060ac1b82293c)) - Barry Walker, Claude Opus 5 (1M context)
- add Phase 2b plan for interrupts, RDY and SO - ([995aed6](https://github.com/barryw/SixtyFiveXX/commit/995aed6d3893c2a9f67c881f7e89523feb038c02)) - Barry Walker, Claude Opus 5 (1M context)
- add Phase 2a plan for the 105 undocumented NMOS opcodes - ([af88a0c](https://github.com/barryw/SixtyFiveXX/commit/af88a0cc902ffe58887b2090f967931f3d63586a)) - Barry Walker, Claude Opus 5 (1M context)
- revise spec for one shared engine across all five cores - ([ecd444d](https://github.com/barryw/SixtyFiveXX/commit/ecd444daf44dcb42a6fe1b8413c339814a163e8a)) - Barry Walker, Claude Opus 5 (1M context)
- add Phase 1 implementation plan for the 6502 core - ([33033bb](https://github.com/barryw/SixtyFiveXX/commit/33033bb80ee035f12044b0a5958f7256bee03f5c)) - Barry Walker, Claude Opus 5 (1M context)
- add SixtyFiveXX design spec and MIT licence - ([44efb85](https://github.com/barryw/SixtyFiveXX/commit/44efb8558c53c06fb2a8ae987a8c552cff4fd0e9)) - Barry Walker, Claude Opus 5 (1M context)
#### Tests
- gate the disassembler on 64tass and on the engine - ([16df23e](https://github.com/barryw/SixtyFiveXX/commit/16df23e815c1d2bc38e9c42dedafc9cfe949e45c)) - Barry Walker
- gate the 6510's port on VICE's cpuport/test1 - ([d899539](https://github.com/barryw/SixtyFiveXX/commit/d8995399b33242d185d3e801e31cf05b7259b5bb)) - Barry Walker
- exempt one generator-specific address from the Harte comparison - ([d33b7f8](https://github.com/barryw/SixtyFiveXX/commit/d33b7f89ee78c774fe3bc2e737c59eeef8a33ec7)) - Barry Walker, Claude Opus 5 (1M context)
- gate the public surface of the packed assembly - ([558906d](https://github.com/barryw/SixtyFiveXX/commit/558906dddaa2761db368a9639858b7b5ff8c45b7)) - Barry Walker, Claude Opus 5 (1M context)
- assert the 6502 variant declares its CpuVariant value - ([32e0905](https://github.com/barryw/SixtyFiveXX/commit/32e09050d3cae0784af6f125331f491fa2ea82a7)) - Barry Walker, Claude Opus 5 (1M context)
- run both test suites against net8.0 and net10.0 - ([119163e](https://github.com/barryw/SixtyFiveXX/commit/119163efebd7a354d27eb63f5544c89a146a85d1)) - Barry Walker
- close final-review gaps in interrupt, RDY and jam coverage - ([7b55964](https://github.com/barryw/SixtyFiveXX/commit/7b559643cead6d54e4cdb1bf4b2f98ae91b25f44)) - Barry Walker, Claude Opus 5 (1M context)
- assert the NMOS BRK/NMI hijack trap in the Klaus interrupt gate - ([728a9cb](https://github.com/barryw/SixtyFiveXX/commit/728a9cb04ceedc2841599f1d6fa060692215d922)) - Barry Walker, Claude Opus 5 (1M context)
- run Klaus Dormann's interrupt test as an independent gate - ([a438316](https://github.com/barryw/SixtyFiveXX/commit/a438316a0c52cc350936d8b9e42b381abd72b4d6)) - Barry Walker, Claude Opus 5 (1M context)
- make the IRQ level-sensitivity test discriminate from a latch - ([651170f](https://github.com/barryw/SixtyFiveXX/commit/651170f9c390b429b11fb331d659a682f7056699)) - Barry Walker
- add the Klaus Dormann functional test as a second conformance gate - ([2261f3b](https://github.com/barryw/SixtyFiveXX/commit/2261f3b453a557319e65a233517edec22a48d9fa)) - Barry Walker
- widen the Harte conformance gate to all 256 opcodes - ([007f4b6](https://github.com/barryw/SixtyFiveXX/commit/007f4b6ef4673836b13a0e31de2751c3810e7688)) - Barry Walker
- add Harte SingleStepTests conformance gate for legal 6502 opcodes - ([293e220](https://github.com/barryw/SixtyFiveXX/commit/293e2207aba517cf6d9015ad107fcc6a159d0db1)) - Barry Walker
- cover the nine untested addressing mode and access combinations - ([7539e51](https://github.com/barryw/SixtyFiveXX/commit/7539e5116238698596816683d1f7c0c9180f33c5)) - Barry Walker
#### Build
- adopt the house cog configuration - ([bf0889d](https://github.com/barryw/SixtyFiveXX/commit/bf0889d88e6c1a278fefdb6b921f7fcfa2652bfd)) - Barry Walker
- multi-target net8.0 and net10.0 and add package metadata - ([eb94263](https://github.com/barryw/SixtyFiveXX/commit/eb942635fb544fa637a1d90d7febb9e3ac27f103)) - Barry Walker
- add version stamping for Directory.Build.props - ([a60f1c5](https://github.com/barryw/SixtyFiveXX/commit/a60f1c5ccbe9683bff5d92e66e2d0a8d940af148)) - Barry Walker
- port Klaus Dormann's interrupt test from AS65 to 64tass - ([e2dbfc4](https://github.com/barryw/SixtyFiveXX/commit/e2dbfc4b102f8cf2b193f603b0c99cfc5a62d2d7)) - Barry Walker
#### CI/CD
- cache the conformance vectors, and make writing them concurrency-safe - ([ee8554c](https://github.com/barryw/SixtyFiveXX/commit/ee8554cf34d23f4bf56452b568efaf3321429d8c)) - Barry Walker, Claude Opus 5 (1M context)
- onboard to the woodpecker-release config service - ([c16efa5](https://github.com/barryw/SixtyFiveXX/commit/c16efa5e4e7bf78544b3bde14f5ed40f7354fdd8)) - Barry Walker
- add the SixtyFiveXX CI image - ([be754c9](https://github.com/barryw/SixtyFiveXX/commit/be754c9da7f662f1ff7bc17d87c78bc97f615018)) - Barry Walker
#### Refactoring
- make the Harte harness generic over the variant - ([015435b](https://github.com/barryw/SixtyFiveXX/commit/015435b7d5104c77d37d56f77929f0fd0da0d895)) - Barry Walker, Claude Opus 5 (1M context)
- introduce ICpuVariant and build micro-op tables per variant - ([14e3693](https://github.com/barryw/SixtyFiveXX/commit/14e3693c1e50158ce71d561d2649640dac2c76e5)) - Barry Walker
- rename IrqLine/NmiLine readbacks to IrqAsserted/NmiAsserted - ([49a355c](https://github.com/barryw/SixtyFiveXX/commit/49a355ca522b05ac3d602b3da1e95dfca0ef370e)) - Barry Walker
- apply Phase 2a final review findings before merge - ([d29500f](https://github.com/barryw/SixtyFiveXX/commit/d29500f2dbe0cc8bc4d4bcca8b41a8d99c0d9401)) - Barry Walker, Claude Opus 5 (1M context)
- apply final review findings before merge - ([bd35eac](https://github.com/barryw/SixtyFiveXX/commit/bd35eacd8c0286dd42d09cde6f259ef8f3af2513)) - Barry Walker
- declare CPU scratch fields on first use instead of up front - ([ca80a76](https://github.com/barryw/SixtyFiveXX/commit/ca80a76167f509b5b80a5d13ac90c27f3a59913c)) - Barry Walker, Claude Opus 5 (1M context)

- - -

Changelog generated by [cocogitto](https://github.com/cocogitto/cocogitto).
# Third-party notices

The native DLL links the author's unmodified SPS implementation and its numerical dependencies. Sources are downloaded by `scripts/bootstrap.ps1`; the source repositories retain their own license and copyright notices. The build copies the applicable license files into `artifacts/SequentialGallery/licenses/`.

| Dependency | Source | Version |
|---|---|---|
| Sequential Gallery / SPS | https://github.com/yuki-koyama/sequential-gallery | cfbf7955ad4b47bb3d3a6854d0b93f643bf20f61 |
| Sequential Line Search | https://github.com/yuki-koyama/sequential-line-search | cfdc4f14b5d1e2db780ca68fbae1b70e835504dc |
| Eigen | https://github.com/eigenteam/eigen-git-mirror | 3.3.7 / cf794d3b741a6278df169e58461f8529f43bce5d |
| mathtoolbox | https://github.com/yuki-koyama/mathtoolbox | 2fd2302e65768f484e4b81fafb9be7129e1d161c |
| NLopt | https://github.com/stevengj/nlopt | 7d04da19236148f9ae9d8dfa470a2264f5d2aa33 |
| nlopt-util | https://github.com/yuki-koyama/nlopt-util | 4dc295323d2edefb0bb16d9e6e9eb9d5f958a845 |
| parallel-util | https://github.com/yuki-koyama/parallel-util | 7afedd6ab31edadad80980b06d43e4c4d12810e5 |
| timer | https://github.com/yuki-koyama/timer | 559def88266105ba149c11501e7a99327696ae07 |

Sequential Gallery is Copyright (c) 2021 National Institute of Advanced Industrial Science and Technology (AIST), licensed under MIT. See the copied upstream LICENSE for the full permission notice. Eigen license texts and the source above are provided for its MPL requirements. CMake is a build-time tool, not linked into the plugin.

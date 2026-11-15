# GPU color backends and qualification

The 2.2 preview offers standard Windows gamma curves for compatible SDR display drivers, including independent RGB gain and an approximate temperature tint. NVIDIA Digital Vibrance uses the existing NvAPIWrapper backend. HDR/advanced-color outputs are guarded because legacy gamma writes are not a supported HDR color pipeline.

AMD exposes display custom color, including saturation, through ADLX. Its official sample demonstrates feature-support queries and display-specific settings: [AMD DisplayCustomColor](https://gpuopen.com/manuals/adlx/programming-with-adlx/adlx-samples/c-samples/display/displaycustomcolor/), [ADLX display interfaces](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IDisplaySettings.h).

Intel offers IGCL as a separate graphics-driver API with display controls and capability-dependent properties. Driver binaries, native structures, handle lifetime and supported controls must be qualified for the actual driver/display combination: [IGCL API](https://intel.github.io/drivers.gpu.control-library/Control/api.html), [Intel IGCL repository](https://github.com/intel/drivers.gpu.control-library).

The available machine has an NVIDIA GPU; the user confirmed no AMD or Intel test system is available. Additional proprietary AMD/Intel saturation backends are therefore **not implemented or advertised as supported**. The Workspace hardware panel states this limitation explicitly. Implementing and qualifying those native SDK bindings remains the outstanding part of H08. The common Windows gamma path is separate and is not evidence of ADLX/IGCL support.

Before adding either backend, verify native ABI/version negotiation, unique Windows-display association, individual capability flags, before/after snapshots, combined-update rollback, HDR behavior and driver-reset recovery on that vendor's hardware. Record real results in the hardware qualification matrix.

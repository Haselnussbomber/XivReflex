<img align="left" src="XivReflex/Assets/Icon.png" width="60px" height="60px" alt="XivReflex"/>

**XivReflex** is a lightweight plugin that adds NVIDIA Reflex Low Latency to the game.<br/>
<br/>
<hr>

FFXIV dispatches its framework and render threads simultaneously, which causes inputs to be polled at the start of a frame and become stale by the time the GPU actually finishes drawing, especially under heavy GPU load.

This plugin hooks into the main game loop to delay the framework tick until right before the GPU is ready for the next frame. By sampling your keyboard and mouse at the last possible microsecond, it eliminates DirectX render queue backlog and significantly reduces click-to-pixel latency during GPU-bound scenarios.

**Requirements:**

- NVIDIA GeForce 900 Series or higher
- Driver version 456.38 or higher

---

Note: This plugin supports NVIDIA Reflex Low Latency; it does not support NVIDIA Reflex 2 Frame Warp.

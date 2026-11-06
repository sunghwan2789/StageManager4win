# Windows Graphics Capture and DirectComposition spike results

Date: 2026-11-06
Environment: Windows 11 Pro build 26200 (VMware Fusion VM), .NET 10, `net10.0-windows10.0.22621.0`, CsWin32 0.3.367.

The throwaway spike lived in `spike/StageManager4win.Spike/` and was deleted after this note was written.

## Question 1: Does CsWin32 generate the required interop?

Yes. CsWin32 generated every requested member, including:

- `DCompositionCreateDevice` (generic `DCompositionCreateDevice<T>` overload works)
- `IDCompositionDevice`, `IDCompositionTarget`, `IDCompositionVisual`, `IDCompositionSurface`, `IDCompositionRectangleClip`
- `D3D11CreateDevice`, `ID3D11Device`, `ID3D11DeviceContext`, `ID3D11Texture2D`, `ID3D11ShaderResourceView`, `ID3D11SamplerState`, `ID3D11VertexShader`, `ID3D11PixelShader`
- `D3DCompile`, `RoGetActivationFactory`, `WindowsCreateString`, `WindowsDeleteString`
- `IGraphicsCaptureItemInterop`, `CreateDirect3D11DeviceFromDXGIDevice`
- `SetWindowRgn`, `CreateRectRgn`, `GetDpiForWindow`, `WindowFromPoint`

Two members are usable only with extra work:

1. `ID3D11VertexShader` and `ID3D11PixelShader` are returned from `CreateVertexShader`/`CreatePixelShader` as `ID3D11VertexShader_unmanaged*`/`ID3D11PixelShader_unmanaged*`. The pointer must be queried for the public interface and released:

```csharp
ID3D11VertexShader_unmanaged* raw = null;
_device.CreateVertexShader(blob.GetBufferPointer(), blob.GetBufferSize(), null, &raw);
try
{
    raw->QueryInterface(out ID3D11VertexShader shader).ThrowOnFailure();
    return shader;
}
finally
{
    raw->Release();
}
```

2. A WGC `IDirect3DSurface` cannot be cast or QI'd directly to `ID3D11Texture2D` (`E_NOINTERFACE`). It must be unwrapped through `IDirect3DDxgiInterfaceAccess`, which is not in CsWin32 or the Windows SDK projection, so it was hand-written:

```csharp
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    void GetInterface([In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}
```

The SPIKE-ONLY carpet bomb `CreateDirect3D11DeviceFromDXGIDevice` / WinRT `IDirect3DDevice` path is not needed for the final implementation; WGC accepts the native D3D11 device through the interop factory.

No other member was missing or unusable.

## Question 2: DComp surface pixel size to DIP mapping

`dips = pixels * K`, where `K = 96 / dpi`.

Measured at 125% scaling (`dpi = 120`):

- A 200×100-pixel surface was displayed at exactly 200×100 physical pixels, i.e. 160×80 DIPs.
- `K = 0.8` (two decimal places: `0.8, 0.8`).
- The surface origin behaved the same way: a tile placed at physical (100, 100) covered physical (100, 100)–(300, 200).

At 100% (`dpi = 96`), `K = 1` and DIPs equal pixels.

The planned 200% check could not be performed on this VM: the display is 1187×891 and Settings offers only 100% and 125% scaling. `EnumDisplaySettings` enumerated 0 modes and the resolution combo exposed no additional modes, so the display could not be raised to a resolution that offers 200%. The formula `K = 96 / dpi` is confirmed by the 125% measurement; at 200% (`dpi = 192`) it extrapolates to `K = 0.5`.

Consequence for Tasks 12 and 15: DComp surface coordinates and surface sizes are physical pixels. Layout is computed in DIPs and must be converted with `physical = dip / K = dip * dpi / 96`, on the UI thread that owns the DComp device.

## Question 3: Click-through and activation

Both behaved as required.

- Empty part of the overlay window: `WindowFromPoint(600, 500)` returned the window below (`0xF04DC`, explorer), not the overlay. Clicks pass through the transparent region.
- Red tile: `WindowFromPoint(200, 150)` returned the overlay (`0x8053A`/`0x1304AC` in separate runs). A synthetic left click on the tile produced `WM_MOUSEACTIVATE` (0x0021) and `WM_LBUTTONDOWN` (0x0201) on the overlay, and the foreground window stayed `0xE0510` (explorer): `overlay_activated = False`.
- `WS_EX_NOACTIVATE` plus the window region are sufficient; no `WM_NCHITTEST` override was needed for this behavior.

## Question 4: WGC yellow capture border

No. On build 26200, `session.IsBorderRequired = false` was accepted and no yellow border appeared around the captured target (pixel scan of the target's surroundings found no saturated yellow; the only captures-border-like color was the ordinary window chrome).

The app should still set `IsBorderRequired = false` but must not depend on it being honored on other machines; a rejected set should be caught and ignored.

## Question 5: Measured capture frame rate

10-second run at 100% scaling, 300×220 target window:

- 364 frames / 10.67 s = **34.1 fps** (an earlier run: 347 frames / 10.66 s = 32.6 fps).
- Maximum frame gap: 95 ms in the steady run; one earlier run saw a single 388 ms gap.
- Frames continued to arrive while only the damaged tile region was updated; last frame persisted after `StopCapture`.

## Additional findings that Tasks 12 and 15 must account for

- `WindowsCreateString` needs the string length; passing `0` produces an empty HSTRING and `RoGetActivationFactory` fails with `E_INVALIDARG`.
- `Direct3D11CaptureFramePool.FrameArrived` args are an empty `object`, not the frame. Use `sender.TryGetNextFrame()` and dispose the frame.
- `FrameArrived` is raised on a free-threaded capture thread. DComp surface calls from that thread fail with `E_NOINTERFACE` for `IDCompositionSurface`; rendering must be marshaled to the UI/STA thread (the spike used `BeginInvoke` with a drop-pending flag). Capture session and pool creation/disposal also belong on the UI thread; creating and disposing a second capture on a background thread hung.
- `IDCompositionTarget` must be held in a field. Otherwise GC releases it and the visual detaches from the window (the tile disappeared).
- The full-screen-triangle pixel shader must force alpha to 1 (`float4(sample.rgb, 1)`) and the context must set `IASetPrimitiveTopology(TRIANGLELIST)` before `Draw(3, 0)`; otherwise nothing visible is drawn.
- `IDCompositionSurface.BeginDraw` can return a non-zero update offset (observed `{1, 1}` and `{203, 1}`). Set the D3D viewport to the returned offset.
- Cache per-frame D3D views (RTV/SRV) instead of creating them per frame; the spike leaked them and real playback would exhaust resources.
- WGC item size may differ from the window's outer bounds (300×220 window produced a 286×213 item). Use `item.Size` and handle resize/`Closed`.
- Capturing a second app (Notepad, 1036×542) worked, but tearing down that secondary session/pool hung. Task 15's `CaptureLeaseTracker` tests must cover start/stop of more than one capture session on a single UI thread.

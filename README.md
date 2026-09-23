# NEUROSHELF

**A VR simulation that measures how in-store marketing shapes what you buy — then shows you your own data.**

Unity 6.3 LTS · URP · OpenXR · C#

---

## Why

Advertising techniques work precisely because people do not notice them. Being told about them rarely changes behaviour — the listener agrees in theory and keeps responding to the same cues in practice.

NEUROSHELF closes that gap by putting the participant under the influence first, in controlled conditions, and showing them the recording afterwards. The output is their own effect size, not someone's opinion.

---

## How it works

| Phase | What happens | What is recorded |
|---|---|---|
| **A — Naive run** | Shopping from a list on a fixed budget. Manipulations active; participant not told. | Gaze dwell time, path, pickups and returns, basket total, impulse share |
| **B — Debrief** | A separate analytics room replays the route, lights a gaze heatmap on the shelves, annotates every purchase with the technique that drove it. | — |
| **C — Aware run** | Same store, manipulations disabled: neutral planogram, honest price tags, no scarcity timers. | Same metrics, compared against Phase A |

---

## Techniques modelled

Each is an independent module, toggled through an experiment-condition asset. Conditions are data, not code.

| Technique | Implementation | Evidence |
|---|---|---|
| Eye-level placement | Target brand at the 160 cm tier, competitors at 60 cm | Chandon et al. (2009) · Drèze et al. (1994) |
| Shelf facings | 4 facings for the target brand vs 1 for the competitor | Chandon et al. (2009) |
| Price anchoring | Struck-through reference price beside the actual price | Tversky & Kahneman (1974) |
| Decoy effect | Three sizes where the middle exists only to sell the largest | Huber, Payne & Puto (1982) |
| Scarcity & urgency | "3 left" tags, countdown timers on promo stands | Worchel, Lee & Adewole (1975) |
| Social proof | NPC shoppers cluster at the promoted shelf | Cialdini (1984) |
| Music priming | Background soundtrack shifts category preference | North, Hargreaves & McKendrick (1999) |
| Mere exposure | Brand logo shown *N* times before the shelf is reached | Zajonc (1968) |
| Lighting accent | Warm 3000K over impulse zone, cold 5000K over markdowns | Experimental variable |

---

## Known limitations

Stated plainly, because this is a measurement instrument and its boundaries matter.

- **No true eye tracking.** Head direction is recorded as a coarse proxy. A participant can move their eyes without turning their head — the metric is *head-gaze*, not eye-gaze.
- **No olfactory channel.** Scent marketing cannot be reproduced in VR.
- **No physiological signals.** EEG and GSR are out of scope, though the analytics layer is event-driven and accepts additional sources without modification.
- **Headset-free development.** Built and tested on the XR Device Simulator; see below for the calibration this required.

---

## Scene

Built at real-world scale — 1 unit = 1 metre. The proportions are the experiment, not decoration.

| Element | Dimensions |
|---|---|
| Sales floor | 12 × 8 × 3 m |
| Shelf unit (×4) | 2.4 × 0.6 × 1.8 m |
| Shelf tiers | **0.60 m** requires bending · **1.10 m** mid · **1.60 m** eye level |
| Aisle between rows | 2.4 m clear — room-scale turning space |
| Checkout counter | 2 × 1.1 × 0.8 m |
| Player spawn | (0, 0, −3.2), facing the sales floor |

---

## Texture atlas

A single 2048×2048 atlas on a 4×4 grid carries the entire product range plus four environment materials — one material for everything on the shelves.

| # | Content | # | Content |
|---|---|---|---|
| 0–3 | Metal · Plastic · Wood · Floor tile | 8–11 | KRISP · NORDA · GRANO · FERRO |
| 4–7 | AURA · VOLTA · NUBO · ZEST | 12–15 | MIRA · OKTA · LUMEN · PURA |

Each cell occupies a 0.25 × 0.25 UV range, giving 512 px per product — enough for the label to read at arm's length, which is as close as anyone gets to a shelf in this scene.

Maps: `Albedo` (sRGB), `Normal`, and `MetallicSmoothness` — metallic in RGB, smoothness in alpha, as URP expects. Separate `Metallic` and `Roughness` maps are kept for documentation.

**All brands are fictional.** No real trademarks are used. Package design is itself an experimental variable: colour, contrast and price-font size affect shelf salience.

---

## Tooling

Two scripts replace the manual click-work and make the asset pipeline reproducible.

**`NEUROSHELF_Blender_LR2.py`** — builds 12 packaging models at real dimensions, runs Smart UV Project with a 0.01 island margin on each, packs every unwrap into its assigned atlas cell by UV arithmetic, and exports a single FBX with a selection filter.

**`NeuroshelfMaterialSetup.cs`** — an Editor menu that sets correct import settings per texture (sRGB for colour, linear for data, Normal Map type, Clamp wrap for atlas safety), builds the URP Lit material, wires the maps into the right slots, applies it across a hierarchy, and reports material and renderer counts for draw-call analysis.

---

## Architecture

```
Assets/_Project/
├── Scenes/         00_Bootstrap · 01_MainMenu · 02_Store · 03_DebriefLab
├── Scripts/
│   ├── Core/           session and phase control
│   ├── Data/           ScriptableObject definitions
│   ├── Interaction/    products, shelves, cart, price tags
│   ├── Manipulation/   one module per technique behind IManipulator
│   ├── Analytics/      gaze, path, purchase logging → CSV export
│   ├── NPC/            shopper agents, IK promoter
│   └── UI/             wrist panel, debrief screens, heatmap
├── Art/            Models · Textures
├── Materials/
├── Prefabs/
└── Configs/        experiment conditions, planograms, product catalogue
```

Two decisions carry the design.

**Experiment conditions live in data, not code.** A condition asset lists which manipulators are active, which planogram to load, which audio and lighting to apply. Switching between the manipulated and neutral runs swaps one asset — no duplicated scenes, no branching logic.

**Analytics is event-driven and one-directional.** The session recorder subscribes to interaction events and never calls back into gameplay. Logging can be removed entirely without affecting the simulation; new data sources attach through the same subscription.

---

## Running without a headset

No HTC Vive on hand, so the project runs on the **XR Device Simulator** from the XR Interaction Toolkit samples. The interaction logic is real; only the input source is substituted.

Open `Assets/_Project/Scenes/02_Store.unity`, press Play. Keyboard layout must be English.

| Key | Action |
|---|---|
| `Tab` | Cycle: head → left controller → right controller |
| `W A S D` | Move |
| Mouse | Look / aim |
| `Shift` / `Space` | Down / up |

### Eye-height calibration

The simulator does not report an anthropometrically correct head height. At XR Origin defaults the eye line landed on the **110 cm** tier — meaning the eye-level effect, the central independent variable of this project, did not reproduce at all.

`Camera Y Offset` is therefore calibrated so the camera's world Y reads **1.65 m** in Play mode, matching the 165–175 cm stature the scene is designed around. Verified by reading the Main Camera's world position, not by eye.

Worth knowing before touching the XR Origin: tier heights are experimental parameters and only mean anything relative to a correct eye height.

---

## Roadmap

| Stage | Scope | Status |
|---|---|---|
| **1** | Environment, blockout geometry, scene hierarchy, XR foundation | ✅ Done |
| **2** | UV pipeline, texture atlas, PBR materials, Editor tooling | ✅ Done |
| 3 | Lighting architecture, lightmap baking, VR performance budget | In progress |
| 4 | Shopper agents, skeletal animation, procedural IK | Planned |
| 5 | Grab, ray and socket interaction | Planned |
| 6 | Analytics layer, CSV export, debrief room | Planned |

---

## Third-party assets

All third-party resources are **CC0**. Attribution is given as a matter of practice, not licence requirement.

[Kenney](https://kenney.nl/assets) · [Quaternius](https://quaternius.itch.io/) · [Poly Haven](https://polyhaven.com/textures) · [ambientCG](https://ambientcg.com/)

Original to this project: all packaging models, the texture atlas and every map in it, label design, and both automation scripts.

---

## References

1. Chandon, P., Hutchinson, J. W., Bradlow, E. T., & Young, S. H. (2009). Does In-Store Marketing Work? *Journal of Marketing*, 73(6), 1–17.
2. Drèze, X., Hoch, S. J., & Purk, M. E. (1994). Shelf Management and Space Elasticity. *Journal of Retailing*, 70(4), 301–326.
3. Tversky, A., & Kahneman, D. (1974). Judgment under Uncertainty. *Science*, 185(4157), 1124–1131.
4. Huber, J., Payne, J. W., & Puto, C. (1982). Adding Asymmetrically Dominated Alternatives. *Journal of Consumer Research*, 9(1), 90–98.
5. Worchel, S., Lee, J., & Adewole, A. (1975). Effects of supply and demand on ratings of object value. *JPSP*, 32(5), 906–914.
6. North, A. C., Hargreaves, D. J., & McKendrick, J. (1999). The influence of in-store music on wine selections. *Journal of Applied Psychology*, 84(2), 271–276.
7. Zajonc, R. B. (1968). Attitudinal effects of mere exposure. *JPSP*, 9(2, Pt.2), 1–27.
8. Meißner, M., Pfeiffer, J., Pfeiffer, T., & Oppewal, H. (2019). Combining virtual reality and mobile eye tracking for shopper research. *Journal of Business Research*, 100, 445–458.

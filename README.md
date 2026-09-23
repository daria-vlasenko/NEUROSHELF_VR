# NEUROSHELF — VR Simulation of Advertising Psychology & Neuromarketing

An immersive research instrument that measures how in-store marketing techniques shape purchasing decisions — then shows the participant their own behavioural data.

> Coursework project for **3D Modelling & Virtual Reality**.
> Specialisation: Software Engineering — Engineering Psychology of Information Technology.
> Built across five lab assignments; currently at Lab 1.

---

## The idea

Advertising techniques work precisely because people do not notice them. Being told about them in a lecture rarely changes behaviour — the listener agrees in theory and keeps responding to the same cues in practice.

NEUROSHELF closes that gap in three phases:

| Phase | What happens | What is measured |
|---|---|---|
| **A — Naive run** | The participant shops from a list with a fixed budget. Manipulations are active; they are not told. | Gaze dwell time, path, pickups and returns, basket total, impulse share |
| **B — Debrief** | A separate analytics room replays their route, lights a gaze heatmap on the shelves, and annotates every purchase with the technique that drove it. | — |
| **C — Aware run** | The same store with manipulations disabled: neutral planogram, honest price tags, no scarcity timers. | The same metrics, compared against Phase A |

The output is the participant's own effect size, not an opinion.

---

## Techniques modelled

Each technique is an independent module, switched on or off through an experiment-condition asset — conditions are configurable without touching code.

| Technique | Implementation | Evidence |
|---|---|---|
| Eye-level placement | Target brand on the 160 cm tier, competitors at 60 cm | Chandon et al. (2009); Drèze et al. (1994) |
| Shelf facings | 4 facings for the target brand vs 1 for the competitor | Chandon et al. (2009) |
| Price anchoring | Struck-through reference price beside the actual price | Tversky & Kahneman (1974) |
| Decoy effect | Three sizes where the middle option exists only to sell the largest | Huber, Payne & Puto (1982) |
| Scarcity & urgency | "3 left" tags, countdown timers on promotional stands | Worchel, Lee & Adewole (1975) |
| Social proof | NPC shoppers cluster at the promoted shelf | Cialdini (1984) |
| Music priming | Background soundtrack shifts category preference | North, Hargreaves & McKendrick (1999) |
| Mere exposure | Brand logo shown *N* times before the shelf is reached | Zajonc (1968) |
| Lighting accent | Warm 3000K over the impulse zone, cold 5000K over markdowns | Implemented as an experimental variable |

---

## Known limitations

Stated explicitly, because the project is a measurement instrument and its boundaries matter:

- **No true eye tracking.** Head direction is recorded as a coarse proxy for gaze. A participant can move their eyes without turning their head; the metric is *head-gaze*, not eye-gaze.
- **No olfactory channel.** Scent marketing cannot be reproduced in VR.
- **No physiological signals.** EEG and GSR are out of scope, though the analytics layer is event-driven and accepts additional sources without modification.
- **No headset available.** Development and testing run on the XR Device Simulator — see *Running without a headset* below.

---

## Tech stack

| | |
|---|---|
| Engine | Unity 6.3 LTS (6000.3.24f1) |
| Render pipeline | Universal Render Pipeline |
| Language | C# |
| XR | OpenXR, HTC Vive Controller Profile |
| Interaction | XR Interaction Toolkit 3.3.2 |
| Headless testing | XR Device Simulator |
| 3D authoring | Blender |

URP was chosen over the Built-in pipeline because the development machine has integrated graphics and no discrete GPU — URP is the pipeline Unity targets at exactly this hardware class, and it is the standard pipeline for VR.

---

## Running without a headset

No HTC Vive is available, so the project is driven by the **XR Device Simulator** from the XR Interaction Toolkit samples. The interaction logic is real; only the input source is substituted.

1. Open `Assets/_Project/Scenes/02_Store.unity`
2. Press **Play**
3. Controls — keyboard layout must be **English**:

| Key | Action |
|---|---|
| `Tab` | Cycle control: head → left controller → right controller |
| `W A S D` | Move |
| Mouse | Look / aim controller |
| `Shift` / `Space` | Down / up |

### Eye-height calibration

The simulator does not report an anthropometrically correct head height. With the XR Origin defaults the eye line landed on the **110 cm** shelf tier, which meant the eye-level effect — the central independent variable of this project — did not reproduce at all.

`Camera Y Offset` on the XR Origin is therefore calibrated so that the camera's world Y reads **1.65 m** in Play mode, matching the 165–175 cm stature the scene is designed around. The value is verified by reading the Main Camera's world position, not by eye.

This is worth knowing before changing the XR Origin: the shelf tier heights are experimental parameters, and they are only meaningful relative to a correct eye height.

---

## Scene layout

Built to real-world scale — 1 unit = 1 metre. Proportions are not decoration here; the whole experiment rests on them.

| Element | Dimensions |
|---|---|
| Sales floor | 12 × 8 × 3 m |
| Shelf unit (×4) | 2.4 × 0.6 × 1.8 m |
| Shelf tiers | **0.60 m** (requires bending) · **1.10 m** (mid) · **1.60 m** (eye level) |
| Aisle between rows | 2.4 m clear — room-scale turning space |
| Checkout counter | 2 × 1.1 × 0.8 m |
| Player spawn | (0, 0, −3.2), facing the sales floor |

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
├── Prefabs/
├── Art/
├── Materials/
└── Configs/        experiment conditions, planograms, product catalogue
```

Two decisions carry the design:

**Experiment conditions live in data, not code.** A condition asset lists which manipulators are active, which planogram to load, and which audio and lighting to apply. Switching between the manipulated and neutral runs swaps one asset — no duplicated scenes, no branching logic.

**Analytics is event-driven and one-directional.** The session recorder subscribes to interaction events and never calls back into gameplay. Logging can be removed entirely without affecting the simulation, and new data sources attach through the same subscription.

---

## Progress

| Lab | Scope | Status |
|---|---|---|
| **1** | Scene architecture, blockout geometry, hierarchy, XR setup | ✅ Done |
| 2 | UV unwrapping, texture atlas, PBR materials | Planned |
| 3 | Lighting architecture, lightmap baking | Planned |
| 4 | Skeletal animation, Animation Rigging / IK | Planned |
| 5 | Grab, ray and socket interaction | Planned |

### Lab 1 — what was built

- Enclosed room from primitives with correct normal orientation. The ceiling is a `Plane`, rotated 180° on X so its normal faces into the room — without this the ceiling is invisible from inside.
- Four shelf units at 1:1 scale, assembled as a prefab, arranged in two facing rows.
- Scene hierarchy grouped under `Environment` → `Structure` / `Shelves` / `Checkout`.
- OpenXR enabled with the HTC Vive Controller Profile; XR Interaction Toolkit installed; XR Origin placed at the entrance.
- Eye height calibrated and verified numerically (see above).

---

## Note on brands

Every brand, logo, package design and price in this project is fictional. No real trademarks are used. Third-party assets are CC0.

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

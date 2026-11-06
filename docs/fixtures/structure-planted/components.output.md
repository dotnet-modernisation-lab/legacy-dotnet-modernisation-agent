## Shared code

| Component | Layer | Main items | Depends on | Used by |
|---|---|---|---|---|
| (project root) | DataAccess | RepoDal | - | Fx.One: Fx.Logic/Gamma, Fx.Two: Pages |

## Fx.One

| Component | Layer | Main items | Depends on | Used by |
|---|---|---|---|---|
| Fx.Logic/Alpha | not classified | AlphaWorker | Fx.Logic/Beta | Fx.Logic/Beta, Fx.Logic/Gamma |
| Fx.Logic/Beta | not classified | BetaWorker | Fx.Logic/Alpha | Fx.Logic/Alpha |
| Fx.Logic/Gamma | not classified | GammaWorker | Shared code: (project root), Fx.Logic/Alpha | Fx.One/Pages |
| Fx.One/Pages | Presentation | Page.aspx | Fx.Logic/Gamma | - |

## Fx.Two

| Component | Layer | Main items | Depends on | Used by |
|---|---|---|---|---|
| Pages | Presentation | Page.aspx | Shared code: (project root) | - |

## Loops between components

- 2 components depend on each other in a loop, directly or through the others: Fx.One: Fx.Logic/Alpha, Fx.One: Fx.Logic/Beta.

## Presentation code that skips layers

- Page.aspx (presentation code) depends directly on RepoDal (dataaccess) without going through a business or service layer.

## Most depended-on components

- Shared code: (project root) is used by 2 other component(s): changing it affects Fx.One: Fx.Logic/Gamma, Fx.Two: Pages.
- Fx.One: Fx.Logic/Alpha is used by 2 other component(s): changing it affects Fx.Logic/Beta, Fx.Logic/Gamma.
- Fx.One: Fx.Logic/Beta is used by 1 other component(s): changing it affects Fx.Logic/Alpha.
- Fx.One: Fx.Logic/Gamma is used by 1 other component(s): changing it affects Fx.One/Pages.

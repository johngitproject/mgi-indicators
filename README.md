# mgi-indicators

Indicateurs NinjaTrader 8 — recherche sur l'information générée par le marché, les niveaux clés, la volatilité et le delta.

Repo **privé**. Outils de recherche et d'aide à la décision — **pas de signaux, pas de promesse de performance**.

## Modules

| Dossier | Titre vitrine | Fichier (nom technique NT8 inchangé) | Rôle |
|---|---|---|---|
| `mgi/` | MGI Levels | `MGIPriorDayOHLC_Opt.cs` (classe `MGIPriorDayOHLC_Opt`) | Niveaux Prior Day / Week / Month : High, Low, Open, Close + VAH/VAL/VPOC prior jour |
| `open-range/` | Open Range Levels | `MGIOpenRangeLevels.cs` (classe `MGIOpenRangeLevels`) | OR 30s (Second/30 précis) + OR 5min (Minute/1 précis), lignes depuis 14h30, N sessions, Values pour backtest |
| `vol-structure/` | VolT Level | `MGIATRLevels.cs` (classe `MGIATRLevels`) | S/R journaliers depuis Base (Prior Daily Close ou Session Open) ± ATR Wilder daily × 0.5/1/2/3, sessions via templates NT8 |
| `deltaprofile/` | DeltaProfile | `LegToLegDeltaProfile_Opt.cs` (classe `LegToLegDeltaProfile_Opt`) | Profil delta par leg (rotation) ou par session, couche volume profile optionnelle (tick-based) |

Détails par module : voir le `README.md` de chaque dossier.

Version TH (`MGIPriorDayOHLC_TH.cs`), anciennes versions (`DeltaProfile.cs`, `LegToLegDeltaProfile.cs` non-Opt) et `AL2LVP/` volontairement exclus de cette publication.

## Prérequis

- NinjaTrader 8 (Windows)
- Données Day/1 suffisantes pour les modules MGI/ATR (warmup ATR), 1-min ou tick selon module
- Templates de session NT8 configurés (ETH/RTH) pour `vol-structure`

## Installation

Voir `docs/INSTALL.md`. En bref : copier les `.cs` dans `Documents\NinjaTrader 8\bin\Custom\Indicators\`, compiler avec F5, insérer via le panneau Indicators.

## Avertissement

Code de recherche. Aucun backtest publié ici, aucun résultat revendiqué. Les marchés gardent une mémoire testable — ces indicateurs servent à l'observer, pas à la prédire automatiquement.

## Licence

MIT — voir `LICENSE`.

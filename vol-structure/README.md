# VolT Level

Titre vitrine : **VolT Level**.
Fichier : `MGIATRLevels.cs` — classe `MGIATRLevels` (+ enums `MgiAtrSessionMode`, `MgiAtrAnchorSource`, `MgiAtrLabelMode`, `MgiAtrDashStyle` au namespace racine, requis par le codegen NT8).

## Rôle

Structure de volatilité : S/R journaliers calculés depuis une Base (Prior Daily Close ou Session Open) ± ATR Wilder daily × 0.5 / 1.0 / 2.0 / 3.0. Lecture expansion / compression / transition — contexte de régime, pas un signal prédictif.

## Paramètres clés

- `SessionMode` : ETH (journée complète) ou RTH (session cash) — pilote les templates, aucune heure saisie
- `AnchorSource` : PriorDailyClose ou SessionOpen
- `AtrPeriod`, `Min barres daily`, `Nombre de paires S/R` (1–4), `Jours d'historique`
- Requiert données Day/1 (+ Minute/1 en RTH) ; ATR approximé + warning si historique court

## Usage type

Daily + intraday. Niveaux R1–R4 / S1–S4 + base, par session, historique N jours.

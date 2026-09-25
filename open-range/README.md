# Open Range Levels

Titre vitrine : **Open Range Levels**.
Fichier : `MGIOpenRangeLevels.cs` — classe `MGIOpenRangeLevels`.

## Rôle

Niveaux objectifs dérivés de l'open range : OR 30s (calcul précis via série Second/30) + OR 5min (précis via série Minute/1), avec fallback sur la série primaire. Extensions (4 par défaut), lignes tracées depuis 14h30, N sessions d'historique, `Values` exposées pour backtest.

## Paramètres clés

- `OpenTime` (défaut 14:30, référence été) / `EndTime` (21:00), `AdjustForUsDst` (auto +1h hiver US)
- `NumExtensions` (défaut 4), `HistoricalSessions`
- Séries secondaires requises : Second/30 + Minute/1 pour la précision

## Usage type

Intraday (open 14h30 heure chart). Référence breakout/rejet — contexte, pas de promesse de performance.

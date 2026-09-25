# Installation (NinjaTrader 8, Windows)

## 1. Copier les fichiers

Copier les 4 `.cs` (noms inchangés) vers :

```
Documents\NinjaTrader 8\bin\Custom\Indicators\
  MGIPriorDayOHLC_Opt.cs        (depuis mgi/)
  MGIOpenRangeLevels.cs         (depuis open-range/)
  MGIATRLevels.cs               (depuis vol-structure/)
  LegToLegDeltaProfile_Opt.cs   (depuis deltaprofile/)
```

Ne pas renommer : le nom du fichier doit correspondre à la classe pour le codegen NT8.

## 2. Compiler

NinjaTrader → New → NinjaScript Editor → F5 (Compile). Corriger les erreurs éventuelles (conflit si l'ancien `DeltaProfile.cs` / `LegToLegDeltaProfile.cs` est encore présent : les enums non-Opt entrent en collision — supprimer ou déplacer les anciennes versions d'abord).

## 3. Insérer sur un chart

Clic droit chart → Indicators → chercher `MGIPriorDayOHLC_Opt`, `MGIOpenRangeLevels`, `MGIATRLevels`, `LegToLegDeltaProfile_Opt`.

Réglages conseillés :
- MGI Levels : charger assez d'historique (Days to load) pour weekly/monthly
- Open Range : ajouter séries Second/30 et Minute/1 si la précision exacte est voulue
- VolT Level : vérifier le template (ETH/RTH) + données Day/1
- DeltaProfile : activer Tick Replay ; limiter le lookback sur les gros historiques

## 4. Captures

Placer les screenshots dans `docs/screenshots/` (1 par module minimum) — dossiers prêts, images à ajouter.

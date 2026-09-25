# DeltaProfile

Titre vitrine : **DeltaProfile**.
Fichier : `LegToLegDeltaProfile_Opt.cs` — classe `LegToLegDeltaProfile_Opt` (version Opt autonome : enums `ProfileModesOpt`, `VolumeProfileLayerOpt`, `LegRotationModeOpt`, `SessionHoursOpt` incluses, aucune dépendance vers l'ancien `LegToLegDeltaProfile.cs`).

## Rôle

Distribution volume et delta par prix : profil delta par leg basé sur la rotation (nouveau leg quand le prix tourne de X points) OU profil de la session en cours, avec couche volume profile optionnelle (tick-based : derrière / devant / à côté du delta).

Anciens `DeltaProfile.cs` et `LegToLegDeltaProfile.cs` non-Opt exclus — cette version Opt est la version courante.

## Paramètres clés

- `Profile Mode` : LegToLeg / SessionCurrentDay ; `Session Hours` : Chart / Eth / Rth
- `Rotation (Points)` : seuil de nouveau leg (0 = désactivé)
- Rendu delta : largeur max, delta min affiché, couleurs, opacité, texte (visible/adaptatif 12.5k/1.2M), bordures
- Volume Profile : affichage, placement, largeur, opacité
- `Rebuild Lookback Cap`, prints de progression (diagnostic chargement historique)

## Usage type

Chart avec Tick Replay pour Bid/Ask exacts (fallback tick-rule sinon). Masque les barres couvertes en option. Gourmand en ticks — limiter le lookback.

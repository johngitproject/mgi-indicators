# MGI Levels

Titre vitrine : **MGI Levels**.
Fichier : `MGIPriorDayOHLC_Opt.cs` — classe `MGIPriorDayOHLC_Opt` (nom technique volontairement inchangé pour l'import NT8).

## Rôle

Affiche les niveaux des périodes précédentes : Prior Day / Week / Month High, Low, Open, Close, niveaux du jour en cours (DH/DL/DO), et Volume Profile du jour précédent (PDVAH/PDVAL/PDPOC).

Version **Opt** retenue comme version courante. Variante `TH` exclue de cette publication.

## Paramètres clés

- Groupes Daily / Weekly / Monthly : choix des niveaux affichés (PDH/PDL/PDO/PDC, VAH/VAL/VPOC, DH/DL/DO…)
- Couleurs, styles et épaisseurs de lignes par groupe
- Historique : charger assez de jours (Days to load) pour les calculs weekly/monthly

## Usage type

Chart intraday (ex. NQ 1-min), template ETH. Niveaux overlay servant de contexte — pas un signal d'entrée.

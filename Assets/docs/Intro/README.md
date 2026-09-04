# Dossier de travail — Introduction, Recherche et Expéditions

Ensemble complet des éléments de conception produits pour l'introduction du jeu.
Destiné à être déposé dans `Assets/docs/Intro/` sur la branche `Intro`.

---

## Ordre de lecture

**1. `gdd-intro-recherche-expeditions.md`** — la conception.

Document directeur de l'introduction : principes, économie CU, chiffrage complet, déroulé
en treize étapes, système de recherche et ses deux présentations, système d'expéditions,
interface, notes d'implémentation Unity, points encore ouverts.

**2. `ALIGNEMENT_PROJET.md`** — la traduction en valeurs.

Chaque valeur du projet Unity à modifier, avec sa valeur actuelle relevée dans le dépôt et
sa valeur cible. Items, recettes, bâtiments, systèmes, recherches, génération de monde,
brouillard de guerre, tests impactés.

**3. `SPEC_EXPEDITIONS.md`** — le système d'expéditions en détail.

Complète la section 6 du GDD. Modèle de secteur, sondes et unités, les six types de mission
un par un avec objectif, disponibilité, cible, durée, récompense et mode d'échec, règles de
lancement, machine à états, résolution, temps forts scénarisés. La section 10 liste
explicitement les onze points encore ouverts.

**4. `TASK_01_REBALANCE_DATA.md`** — la première tâche. **Réalisée.**

Ticket prêt à passer à Claude Code : rendre l'introduction mesurable avec des changements
de données uniquement. Périmètre explicite, critères d'acceptation, protocole de mesure.

---

## Maquettes

Trois fichiers HTML autonomes, à ouvrir dans un navigateur. Ce sont des maquettes
d'intention, pas des spécifications d'implémentation : elles fixent la structure et le
langage visuel, pas les dimensions exactes.

**`01-menu-recherche-intro.html`** — avant l'amorçage du Datacenter.

Le menu classique et linéaire de la phase de survie : quatre recherches obligatoires en
chaîne, deux optionnelles, un nœud inconnu. Montre les cinq états, la barre de progression
avec le temps restant au débit courant, le débit d'absorption, et la top bar en mode survie
avec son autonomie.

**`02-menu-recherche-apres-amorcage.html`** — après l'amorçage.

Le même menu transformé en réseau neuronal à trois noyaux, dont l'Armement reste éteint
jusqu'à la découverte du premier nid. Les recherches de l'introduction y sont acquises et
rattachées à leur noyau. Le vocabulaire visuel est strictement le même que dans la
maquette 01 — seule la mise en page change, et c'est le point à préserver absolument.

**`03-ecran-lancement-mission.html`**

L'écran de lancement d'expédition : carte dézoomée, rayon d'action, brouillard, infobulle
au survol d'un secteur, panneau de dimensionnement d'escouade avec sa jauge qualitative.
Aucun pourcentage de réussite n'y figure, c'est délibéré.

---

## Ce qui n'est pas dans ce dossier

- `Assets/docs/audit/CURRENT_STATE.md` — l'état réel du code, produit séparément
- `Assets/docs/architecture/*` — architecture, contrats, règles et workflow du projet

Les deux font foi pour tout ce qui touche à l'implémentation. En cas de contradiction
entre le GDD et l'architecture existante, c'est l'architecture qui décide *comment*, et le
GDD qui décide *quoi*.

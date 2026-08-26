# Interaction Core — Line of Sight Occlusion

We should not be able to interact with an object behind a wall.

Le chantier est absorbé par [`interaction-detector.md`](./interaction-detector.md) : le LOS y devient un
prédicat partagé de la couche de détection, et non un système à lui seul.

## État

**Livré** avec la Task 10, sous la forme prévue : `InteractionDetector.HasLineOfSight` est un prédicat de
la classe de base, appelé par les trois détecteurs et évaluable par le pair autoritaire sur une cible
unique. Le détail des décisions d'implémentation vit dans l'« État » de
[`interaction-detector.md`](./interaction-detector.md) ; le résumé utile ici :

- Le ray part du `ViewOrigin` vers l'ancre, sur les seules layers d'occlusion (`OcclusionMask`, défaut
  layer 2 « Occluder »), en excluant le corps de l'interacteur et la géométrie propre de la cible.
- Les rays vivent en `_PhysicsProcess` derrière un cache à hystérésis ; une cible encore inconnue —
  le cas du serveur qui valide une commande one-shot — est castée sur le champ, parce qu'une réponse
  différée refuserait une commande légitime pour une raison invisible.
- Perdre le LOS renvoie `None` et pas `Indicated` : perdre la fenêtre veut dire qu'on regarde ailleurs,
  perdre le LOS qu'il n'y a rien à regarder.
- Occluder est une propriété de l'**occluder**, pas de la cible : un seul `OcclusionMask`, sur le
  détecteur. Un mur porte la layer, une grille qu'on veut traverser ne la porte pas, et l'area découpée
  à la main reste alors seule juge de la visibilité. Aucun interactible n'a d'exemption à déclarer.

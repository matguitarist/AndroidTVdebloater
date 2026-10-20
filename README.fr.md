<p align="center"><img src="docs/icon.png" width="96" alt=""></p>

<h1 align="center">Android TV Debloater</h1>

<p align="center">
  Désactivez les publicités, le pistage et les applications préinstallées de n'importe quel
  Android TV ou Google TV, depuis Windows, macOS ou Linux.<br>
  Une seule fenêtre, des choix expliqués simplement, une annulation en un clic.
</p>

<p align="center"><a href="README.md">English version</a></p>

![Choisir ce qu'il faut désactiver](docs/choose-dark-fr.png)

![Terminé](docs/apply-light-fr.png)

## Ce que fait l'application

- **Trouve votre téléviseur sur le réseau** et s'y connecte en Wi-Fi ou Ethernet. Sans câble, sans root.
- **Liste uniquement ce qui se trouve sur votre téléviseur**, par catégories expliquées simplement :
  pistage, publicités et mode démo, composants système inutilisés, fonctions Google, applications
  préinstallées, ainsi que des réglages de vitesse et de confidentialité.
- **Coche pour vous les choix sans risque.** Ce que vous pourriez utiliser (recherche vocale,
  diffusion, applications de streaming) reste décoché, avec un avertissement sur ce qui ne
  fonctionnera plus.
- **Sauvegarde d'abord, applique, redémarre le téléviseur et le vérifie** : écran d'accueil,
  internet et résolution de noms. Si le DNS anti-pub empêche la résolution, il est annulé sur-le-champ.
- **Annule exactement n'importe quelle opération** depuis la page Restaurer, ou réactive toutes
  les applications en un clic.
- **Propose un écran d'accueil sans publicité** (Projectivy Launcher, FLauncher) : l'ouvre dans le
  Play Store du téléviseur, en fait l'écran d'accueil une fois installé, et peut remettre celui d'origine.
- **Désactive le débogage pour vous** à la fin, puis **Recommencer** pour nettoyer le téléviseur
  suivant. Les téléviseurs déjà utilisés sont listés sur la page Connecter.
- **Les mêmes choix sur le téléviseur suivant** : les choix de la dernière opération sont proposés,
  et peuvent être enregistrés dans un fichier puis chargés sur un autre ordinateur (Windows et Mac
  lisent le même fichier).
- **Libère de l'espace** en vidant les caches des applications, et indique combien.
- **La liste des applications se met à jour toute seule** : les nouvelles entrées de
  [`shared/catalog.json`](shared/catalog.json) arrivent dans l'application sans nouvelle version.
- **Aidez à améliorer la liste** : les applications préinstallées inconnues peuvent être proposées
  sur GitHub en un clic (seulement le modèle et les noms d'applications ; vous voyez tout avant de publier).
- **Se met à jour toute seule** : quand une nouvelle version sort, elle affiche les nouveautés et
  l'installe en quelques secondes (le téléchargement est vérifié avec le SHA-256 publié par GitHub).
  Désactivable.
- **Français et anglais**, modifiable à tout moment. Thèmes clair et sombre selon Windows.

Rien n'est désinstallé. Les applications sont désactivées avec la commande Android `pm disable-user`,
et une réinitialisation d'usine remet aussi tout.

## Télécharger et lancer

Prenez le fichier de votre ordinateur dans la [dernière version](../../releases/latest). Rien
d'autre à installer : la première fois, l'application télécharge les outils ADB officiels de Google
(environ 7 Mo depuis `dl.google.com`) dans son propre dossier. Suivez ensuite les quatre étapes.

| | Fichier | Premier lancement |
|---|---|---|
| **Windows** 10/11 | `AndroidTVDebloater.exe` | SmartScreen peut signaler un éditeur inconnu (application non signée) : **Informations complémentaires → Exécuter quand même**. |
| **macOS** 14+ | `AndroidTVDebloater-mac.zip` | Une vraie application Mac (SwiftUI), pour Apple silicon et Intel, signée et notariée par Apple. Décompressez, placez-la dans Applications et ouvrez-la. Autorisez-la à trouver des appareils sur le réseau local. Les mises à jour arrivent par Sparkle. |
| **Linux** x64 | `AndroidTVDebloater-x86_64.AppImage` | Fonctionne sur la plupart des distributions (Ubuntu 22.04 et plus récent, Fedora, Debian, Mint…). Rendez-le exécutable (`chmod +x`, ou Propriétés → Autoriser l'exécution) et ouvrez-le. Il se met à jour tout seul ; les gestionnaires d'AppImage comme Gear Lever peuvent aussi le mettre à jour. Un programme simple `AndroidTVDebloater-linux-x64` est aussi joint. |

### Sur le téléviseur, avant de commencer

1. **Paramètres → Préférences de l'appareil → À propos** (Google TV : **Paramètres → Système → À propos**).
2. Appuyez 7 fois sur OK sur **Build** (ou **Build de l'OS Android TV**), jusqu'au message
   indiquant que vous êtes développeur.
3. Revenez d'un écran : **Options pour les développeurs → Débogage USB** activé. S'il existe un
   interrupteur **Débogage réseau** ou **ADB sur le réseau**, activez-le aussi.
4. Quand l'application se connecte, acceptez **Autoriser le débogage USB ?** sur le téléviseur
   et cochez *Toujours autoriser*.

Les appareils Google TV récents qui proposent seulement le **Débogage sans fil** avec un code
d'association sont pris en charge depuis la page Connecter.

Une fois terminé, désactivez de nouveau le Débogage USB. Le dernier écran a un bouton qui le fait.

## Appareils pris en charge

Tout ce qui fonctionne sous **Android TV** ou **Google TV** : téléviseurs (Sony, TCL, Xiaomi,
Philips, Hisense…), boîtiers et clés (Chromecast avec Google TV, NVIDIA Shield, Mi Box…) et les
décodeurs Android TV loués par de nombreux opérateurs. Fire TV fonctionne pour la liste
d'applications.

**Non pris en charge :** Samsung (Tizen), LG (webOS), Roku et Apple TV utilisent d'autres systèmes.

## Sécurité

- Seules les applications d'un catalogue vérifié ([`catalog.json`](shared/catalog.json))
  sont proposées, plus celles installées en plus du système (toujours décochées).
- Jamais proposés, quoi que dise le catalogue : toute application pouvant servir d'écran
  d'accueil, le clavier, les Paramètres, le Play Store, les services Google Play, WebView,
  le Bluetooth et le cœur d'Android.
- Chaque changement est inscrit dans un fichier de sauvegarde avec sa valeur d'origine *avant*
  d'être appliqué, et le fichier est enregistré après chaque étape : même une opération
  interrompue peut être annulée.
- « Désactiver le Wi-Fi » n'est proposé que si le téléviseur est branché en Ethernet.
- Les sauvegardes sont dans `%LOCALAPPDATA%\AndroidTVDebloater\backups\<téléviseur>`, avec une
  copie brute de toutes les applications et de tous les réglages.

## Compiler

Application Windows et Linux : nécessite le [SDK .NET 10](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/AndroidTVDebloater
dotnet publish src/AndroidTVDebloater -c Release -r win-x64 -o publish
```

Application Mac (`mac/`, Swift et SwiftUI) : nécessite Xcode, puis `mac/build-app.sh`. Ses textes
viennent de `Strings.cs` : après modification, lancez `python3 tools/gen-mac-strings.py`.

Pousser une étiquette comme `v1.2.0` compile les trois sur GitHub Actions et publie une version ;
le message de l'étiquette devient les notes affichées dans la fenêtre de mise à jour.

## Crédits

Plusieurs entrées du catalogue proviennent des listes d'appareils de
[cleanyourtv](https://github.com/Gathin23/cleanyourtv) (MIT). Voir [NOTICE](NOTICE).

## Licence

MIT. À utiliser à vos risques : l'application ne fait que désactiver et restaurer, mais vous
modifiez votre propre appareil.

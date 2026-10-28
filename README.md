# Tiny11 Builder (Version Française & Améliorée)

Créez une image ISO de Windows 11 allégée, optimisée et prête à l'emploi grâce à une application native WinUI ou aux scripts PowerShell automatisés.

## Nouveautés & Améliorations intégrées

1. **Installation sans surveillance enrichie (`autounattend.xml`)** :
   - Contournement automatique de l'obligation de connexion Internet / Wi-Fi lors de l'OOBE.
   - Suppression des écrans de télémétrie et des suggestions promotionnelles au premier démarrage.
   - Contournement des restrictions matérielles (TPM 2.0, Secure Boot, CPU, RAM) dès la phase d'installation Windows PE.
   - Script post-installation automatique (`SetupComplete.cmd`) pour désactiver les tâches de télémétrie résiduelles.

2. **Intégration directe de pilotes (Drivers)** :
   - Les scripts proposent désormais d'injecter facilement vos dossiers de pilotes (`.inf` pour Wi-Fi, Ethernet, NVMe/VMD Intel/AMD) directement dans l'image système avant la finalisation.

3. **Optimisations Performances & Confidentialité (Gaming & Télémétrie)** :
   - Désactivation du service de télémétrie en arrière-plan `DiagTrack` et `dmwappushservice`.
   - Optimisation de la réactivité système (`MenuShowDelay` à 0).
   - Mode Jeu activé par défaut et désactivation des captures GameDVR consommatrices de ressources en arrière-plan.

4. **Créateur de clé USB bootable UEFI universelle (`Create-Tiny11BootableUSB.ps1`)** :
   - Plus besoin de logiciel tiers comme Rufus : formatez et préparez directement une clé USB en FAT32/UEFI.
   - Découpage automatique de l'image (`install.swm`) via DISM si la taille dépasse 4 Go pour respecter les spécifications UEFI standard.

5. **Navigateurs web & Alternatives Open-Source gratuites** :
   - Choix du navigateur à intégrer au premier démarrage : **Mozilla Firefox**, **Brave Browser**, **Google Chrome**, ou **LibreWolf**.
   - Pré-installation facultative d'outils libres recommandés pour remplacer les applications Microsoft supprimées : **VLC Media Player** (remplace Films & TV), **7-Zip** (compression d'archives), **Notepad++** (éditeur de texte), et **LibreOffice** (suite bureautique).

6. **Options graphiques & Personnalisation de l'interface** :
   - Menu latéral gauche modernisé avec icônes Fluent Segoe MDL2 Assets pour chaque étape.
   - Sélecteur de **Couleur d'accentuation** dynamique en direct (Bleu Windows, Violet Royal, Vert Émeraude, Orange Coucher de soleil, Rouge Carmin, Cyan Néon, Rose Fushia) en complément du thème Clair/Sombre.

7. **Nouvelles options système avancées** :
   - **Bloquer les mises à jour au premier démarrage** : évite que Windows Update s'accapare la connexion et le processeur dès la fin de l'installation OOBE.
   - **Désactiver Windows Defender** : désactive la protection en temps réel, l'analyse comportementale et les services de sécurité hors-ligne pour libérer un maximum de mémoire vive (RAM) et de cycles CPU.
   - **Intégration directe de Defender Control (dControl)** : détecte et embarque automatiquement l'utilitaire portable `Defender Control` dans `C:\Tools\Defender Control` de l'image avec création d'un raccourci sur le Bureau pour réactiver ou désactiver Defender en 1 clic selon vos besoins.

---

## Modes de création

| Mode | Description |
| --- | --- |
| **Tiny11** | Supprime les applications superflues et fonctionnalités indésirables tout en conservant Windows Update, Microsoft Defender et les outils de récupération. |
| **Tiny11 Core** | Épuration maximale : supprime les services de maintenance, la récupération système et désactive Windows Defender. |
| **Personnalisé** | Choisissez individuellement les applications, composants, fonctionnalités et services à retirer ou conserver. |

Le mode **Turbo** accélère le traitement de l'image. Les modes Core et les suppressions du magasin de composants utilisent automatiquement l'installateur Windows classique (Legacy).

> [!WARNING]
> **Tiny11 Core ne peut pas recevoir les mises à jour Windows.** Certaines suppressions protégées en mode personnalisé peuvent rendre Windows instable. Testez toujours votre ISO dans une machine virtuelle (VM) avant toute installation réelle.

---

## Utilisation des scripts PowerShell

1. Ouvrez une invite PowerShell en tant qu'administrateur :
   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   ```
2. Lancez le script de votre choix :
   - Version allégée standard :
     ```powershell
     .\tiny11maker.ps1
     ```
   - Version ultra-minimale :
     ```powershell
     .\tiny11Coremaker.ps1
     ```
3. Suivez les étapes à l'écran (lettre du lecteur ISO monté, index d'édition, injection optionnelle de pilotes).
4. À la fin de la génération, le script vous propose de créer directement votre clé USB bootable avec `scripts\Create-Tiny11BootableUSB.ps1`.

---

## Création d'une clé USB bootable à partir d'une ISO existante

Vous pouvez à tout moment créer une clé USB bootable UEFI pour une ISO déjà générée :
```powershell
.\scripts\Create-Tiny11BootableUSB.ps1 -IsoPath ".\tiny11.iso"
```

---

## Application WinUI 3 compilée

L'application graphique avec toutes les options graphiques et paramètres avancés se trouve dans :
📂 [**`publish_new\Tiny11Builder.exe`**](file:///c:/Users/madtr/Desktop/Tiny11Builder%20fr/publish_new/Tiny11Builder.exe) (ou [**`publish\Tiny11Builder.exe`**](file:///c:/Users/madtr/Desktop/Tiny11Builder%20fr/publish/Tiny11Builder.exe)) (lancement avec élévation administrateur).

---

## Soutenir le projet original

Tiny11 Builder est un projet open-source développé par [NTDEV](https://github.com/ntdevlabs).
- **[Soutien GitHub Sponsors](https://github.com/sponsors/ntdevlabs)**
- **[Don Ko-fi](https://ko-fi.com/ntdev)**

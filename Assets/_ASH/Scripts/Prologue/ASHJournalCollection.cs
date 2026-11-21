using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>t443: what a Grandpa's page gives the player.</summary>
    public enum ASHJournalPageType
    {
        [Tooltip("Survival advice for the area it is found in.")] Tip,
        [Tooltip("Points toward the next area (optional compass marker; can count toward the objective chain).")] Clue,
        [Tooltip("Rare: unlocks a non-starter recipe (JournalPage condition in ASH_PrologueSettings unlock overrides). 1-2 of the 8 pages.")] RareRecipe
    }

    /// <summary>One Grandpa's Journal page: same page format as the Survival Guide torn pages (number, title, text, sketch) plus
    /// gameplay data. Shown in the journal's page reader (Survival Guide tab, GRANDPA'S JOURNAL button). Edit freely (Edit or Play).</summary>
    [Serializable]
    public sealed class ASHJournalCollectionPage
    {
        [Tooltip("Stable id used by saves and unlock conditions (e.g. gj_01).")] public string id = "gj_01";
        [Min(1)] public int number = 1;
        public string title = "Page";
        public ASHJournalPageType type = ASHJournalPageType.Tip;
        [TextArea(3, 10)] public string text;
        [Tooltip("Sketch shown in the page reader (Rook's Grandpa's Journal sketches go here).")] public Sprite sketch;
        [Header("Where")]
        [Tooltip("Area the page is found in (it should fit this area).")] public string area;
        [Tooltip("Where exactly (design note).")] public string location;
        [Tooltip("The next area this page points to.")] public string pointsTo;
        [Tooltip("Placed in the world in the current milestone (else a placeholder spawn point only).")] public bool placed;
        [Header("Clue")]
        [Tooltip("Clue: add a compass marker at markerPosition when found (only when that area exists).")] public bool compassMarker;
        public Vector3 markerPosition;
        [Tooltip("Objective chain flag this page satisfies (documentation; e.g. truckFound for the truck page). Chain steps can also use the flag 'page:<id>'.")] public string chainFlag;
        [Header("Rare recipe")]
        [Tooltip("Recipe asset name unlocked by this page (via the JournalPage condition in ASH_PrologueSettings).")] public string rareRecipeName;

        public string TypeLabel => type == ASHJournalPageType.RareRecipe ? "Rare recipe" : type.ToString();
    }

    /// <summary>A story-page collection separate from the Survival Guide (ASH_FieldManual, untouched). Grandpa's Journal = Resources/ASH_GrandpasJournal
    /// (collection id "grandpa", 8 pages). Recipe conditions of kind JournalPage reference collection id + page id.</summary>
    [CreateAssetMenu(menuName = "ASH/Prologue/Journal Collection", fileName = "ASH_GrandpasJournal")]
    public sealed class ASHJournalCollection : ScriptableObject
    {
        public string collectionId = "grandpa";
        public string title = "Grandpa's Journal";
        [Tooltip("Set by ASH/Systems/Prologue/Set Up Grandpa's Pages; content is only rewritten when below its version (your edits are kept after that).")]
        public int contentVersion;
        public List<ASHJournalCollectionPage> pages = new List<ASHJournalCollectionPage>();
        public ASHJournalCollectionPage Page(string id) { foreach (var p in pages) if (p != null && p.id == id) return p; return null; }

        static ASHJournalCollection _grandpa;
        public static ASHJournalCollection Grandpa { get { if (!_grandpa) _grandpa = Resources.Load<ASHJournalCollection>("ASH_GrandpasJournal"); return _grandpa; } }
        public static ASHJournalCollection Get(string collectionId) { var g = Grandpa; return g && (string.IsNullOrEmpty(collectionId) || g.collectionId == collectionId) ? g : null; }
    }
}

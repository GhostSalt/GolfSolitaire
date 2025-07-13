using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using Wawa.DDL;
using Wawa.Optionals;
using System.Text.RegularExpressions;
using Rnd = UnityEngine.Random;

public class GolfSolitaireScript : MonoBehaviour
{
    static int _moduleIdCounter = 1;
    int _moduleID = 0;

    public KMBombModule Module;
    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMSelectable[] Selectables;
    public SpriteRenderer[] Highlights;
    public SpriteRenderer TemplateCard, OverlayBG;
    public Sprite[] CardSprites;
    public GameObject NoMoreMoves;
    public TextMesh[] OverlayTexts;
    public SpriteRenderer[] JokerRends;
    public MeshRenderer BG;
    public Material[] BGMats;

    private List<SpriteRenderer> CardRendsOnTheTable = new List<SpriteRenderer>();
    private List<int> CardIxsOnTheTable = new List<int>();
    private SpriteRenderer StockCard;
    private SpriteRenderer DeckRend;
    private Stack<int> Deck = new Stack<int>();
    private int[] TableOffsets = new int[] { 28, 28, 28, 28, 28, 28, 28 };
    private int JokerCount, ReadyToContinue, RemainingCards, StockIx;
    private bool CannotContinue, CannotPress = true, InEndgame, NoMoreCards, Solved;
    private Settings _Settings;

    class Settings
    {
        public bool HardMode = false;
        public bool NoBonuses = false;
    }

    void GetSettings()
    {
        var SettingsConfig = new ModConfig<Settings>("GolfSolitaire");
        _Settings = SettingsConfig.Settings; // This reads the settings from the file, or creates a new file if it does not exist
        SettingsConfig.Settings = _Settings; // This writes any updates or fixes if there's an issue with the file
        HardMode = _Settings.HardMode;
        NoBonuses = _Settings.NoBonuses;
        Debug.LogFormat("[Golf Solitaire #{0}] Hard Mode is {1}. Joker bonuses are {2}.", _moduleID, HardMode ? "active" : "disabled", NoBonuses ? "disabled" : "active");
    }

    private bool HardMode, NoBonuses;

    void SortOutMissionDescription()
    {
        string description = Application.isEditor ? "" : Missions.Description.UnwrapOr("");
        var matches = Regex.Matches(description, @"^(?:///? ?)? ?\[Golf ?Solitaire\] (.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count == 0)
        {
            Debug.LogFormat("[Golf Solitaire #{0}] Either nothing has been specified by the mission description, or this module is being played in Free Play.", _moduleID);
            GetSettings();
            if (Application.isEditor)
            {
                HardMode = false;
                NoBonuses = false;
            }
            return;
        }
        if (Application.isEditor)
        {
            HardMode = false;
            NoBonuses = false;
        }
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Groups[1].Value.ToLowerInvariant() == "bonuses on")
            {
                NoBonuses = false;
                Debug.LogFormat("[Golf Solitaire #{0}] Joker bonuses have been enabled, as specified by the mission description.", _moduleID);
            }
            if (matches[i].Groups[1].Value.ToLowerInvariant() == "bonuses off")
            {
                NoBonuses = true;
                Debug.LogFormat("[Golf Solitaire #{0}] Joker bonuses have been disabled, as specified by the mission description.", _moduleID);
            }
            if (matches[i].Groups[1].Value.ToLowerInvariant() == "hard off")
            {
                HardMode = false;
                Debug.LogFormat("[Golf Solitaire #{0}] Hard Mode has been disabled, as specified by the mission description.", _moduleID);
            }
            if (matches[i].Groups[1].Value.ToLowerInvariant() == "hard on")
            {
                HardMode = true;
                Debug.LogFormat("[Golf Solitaire #{0}] Hard Mode has been enabled, as specified by the mission description.", _moduleID);
            }
        }
        Debug.LogFormat("[Golf Solitaire #{0}] Hard Mode is {1}. Joker bonuses are {2}.", _moduleID, HardMode ? "active" : "disabled", NoBonuses ? "disabled" : "active");
    }

    private IEnumerable<List<int>> FindSeriesOfPlays(List<int> cardIxsOnTheTableTemp, int[] tableOffsetsTemp, int stockIxTemp)
    {
        var foundSomething = false;
        for (int i = 0; i < 7; i++)
        {
            if (tableOffsetsTemp[i] >= 0 && DetermineValidity(cardIxsOnTheTableTemp[i + tableOffsetsTemp[i]], stockIxTemp))
            {
                foundSomething = true;
                stockIxTemp = cardIxsOnTheTableTemp[i + tableOffsetsTemp[i]];
                tableOffsetsTemp[i] -= 7;
                var plays = FindSeriesOfPlays(cardIxsOnTheTableTemp, tableOffsetsTemp, stockIxTemp);
                foreach (var play in plays)
                {
                    play.Insert(0, i);
                    yield return play;
                }
            }
        }
        if (!foundSomething)
            yield return new List<int>() { 7 };
    }

    private bool DetermineValidity(int ix, int stockIx)
    {
        if (stockIx == 52 || ix == 52)
            return true;
        if ((stockIx % 13 == 12 || (ix % 13 == 12 && stockIx % 13 != 11)) && HardMode)
            return false;
        var diff = Mathf.Abs((ix % 13) - (stockIx % 13));
        return diff == 1 || diff == 12;
    }

    private bool CheckTPCommandValidity(string command, List<int> cardIxsOnTheTableTemp, int[] tableOffsetsTemp, int stockIxTemp)
    {
        if (command.Contains('d') && NoMoreCards)       //Is the player trying to draw from the deck, when it has no cards? If so, then it's bad.
            return false;
        if (command == "" || command.First() == 'd')    //Is that all of the column presses? If so, it's good.
            return true;
        var ix = int.Parse(command.First().ToString()) - 1;
        if (tableOffsetsTemp[ix] < 0 || !DetermineValidity(cardIxsOnTheTableTemp[ix + tableOffsetsTemp[ix]], stockIxTemp))      //Is the current press invalid? If so, it's bad.
            return false;
        stockIxTemp = cardIxsOnTheTableTemp[ix + tableOffsetsTemp[ix]];
        tableOffsetsTemp[ix] -= 7;
        return CheckTPCommandValidity(command.Length == 1 ? "" : command.Substring(1, command.Length - 1), cardIxsOnTheTableTemp.ToList(), tableOffsetsTemp.ToArray(), stockIxTemp);        //Check the next press.
    }

    private SpriteRenderer SpawnCard()
    {
        var card = Instantiate(TemplateCard, TemplateCard.transform.parent);
        card.transform.localScale = TemplateCard.transform.localScale;
        card.transform.localRotation = TemplateCard.transform.localRotation;
        card.color = TemplateCard.color;
        card.sprite = TemplateCard.sprite;
        return card;
    }

    private Sprite FindCardSprite(int cardIx)
    {
        if (cardIx == -1)
            return CardSprites.Where(x => x.name == "back").First();
        if (cardIx == 52)
            return CardSprites.Where(x => x.name == "joker").First();
        return CardSprites.Where(x => x.name == ("shcd"[cardIx / 13] + new[] { "a", "2", "3", "4", "5", "6", "7", "8", "9", "10", "j", "q", "k" }[cardIx % 13])).First();
    }

    void Awake()
    {
        _moduleID = _moduleIdCounter++;

        for (int i = 0; i < Selectables.Length; i++)
        {
            int x = i;
            Selectables[x].OnHighlight += delegate { Highlights[x].gameObject.SetActive(true); };
            Selectables[x].OnHighlightEnded += delegate { Highlights[x].gameObject.SetActive(false); };
            Highlights[x].gameObject.SetActive(false);
            if (x < 7)
                Selectables[x].OnInteract += delegate { if (!CannotPress) FrontCardPress(x); return false; };
        }
        Selectables[7].OnInteract += delegate { if (!CannotPress) { Selectables[7].AddInteractionPunch(0.5f); StartCoroutine(DealCardToStock()); } return false; };
        Selectables[8].OnInteract += delegate { if (!CannotContinue && !CannotPress) { Selectables[8].AddInteractionPunch(); StartCoroutine(ContinuePressed()); } return false; };
        Module.OnActivate += delegate { StartCoroutine(IntroAnim()); };
    }

    // Use this for initialization
    void Start()
    {
        SortOutMissionDescription();

        BG.material = BGMats[HardMode ? 1 : 0];
        for (int i = 0; i < Selectables.Length; i++)
            Selectables[i].transform.localScale = Vector3.zero;
        TemplateCard.gameObject.SetActive(false);
        DeckRend = SpawnCard();
        DeckRend.transform.localPosition = new Vector3(-0.06f, 0, 0.11f);
        DeckRend.sortingOrder = -1;
        DeckRend.GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().sortingOrder = -3;
        DeckRend.gameObject.SetActive(true);
        NoMoreMoves.SetActive(false);
        OverlayBG.transform.parent.localScale = Vector3.zero;
        StartCoroutine(SpinJokers());
        Initialise();
    }

    void FrontCardPress(int pos)
    {
        Selectables[pos].AddInteractionPunch(0.5f);
        if (!DetermineValidity(CardIxsOnTheTable[pos + TableOffsets[pos]], StockIx))
        {
            Audio.PlaySoundAtTransform("buzzer", transform);
            Debug.Log("Card " + pos + " was unsuccessfully played!");
        }
        else
            StartCoroutine(PlayCard(pos));
    }

    void Initialise()
    {
        var temp = Enumerable.Range(0, 52).ToList();
        for (int i = 0; i < JokerCount; i++)
            temp.Add(52);
        temp = temp.Shuffle();
        foreach (var card in temp)
            Deck.Push(card);
        RemainingCards = 35;
        TableOffsets = new int[] { 28, 28, 28, 28, 28, 28, 28 };
        NoMoreCards = false;
    }

    void CheckForStatemate()
    {
        if (RemainingCards == 0)
        {
            StartCoroutine(Solve());
            Solved = true;
            return;
        }
        if (Deck.Count() != 0)
        {
            CannotPress = false;
            return;
        }
        for (int i = 0; i < 7; i++)
            if (TableOffsets[i] >= 0)
                if (DetermineValidity(CardIxsOnTheTable[i + TableOffsets[i]], StockIx))
                {
                    CannotPress = false;
                    return;
                }
        for (int i = 0; i < Selectables.Length; i++)
            Selectables[i].transform.localScale = Vector3.zero;
        Audio.PlaySoundAtTransform("no valid moves", transform);
        StartCoroutine(Endgame());
    }

    private IEnumerator Solve(float interval = 0.01f)
    {
        float timer = 0;
        while (timer < 0.5f)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        Debug.LogFormat("[Golf Solitaire #{0}] Module solved.", _moduleID);
        Module.HandlePass();
        Audio.PlaySoundAtTransform("solve", transform);
        yield return "solve";
        StartCoroutine(CollectCardFromPiles(StockCard.transform));
    }

    private IEnumerator ContinuePressed(float hideButtonDur = 0.2f, float fadeInDur = 0.4f)
    {
        CannotContinue = true;
        CannotPress = true;
        ReadyToContinue = 0;
        Audio.PlaySoundAtTransform("continue", Selectables[8].transform);
        StartCoroutine(FadeOutText());
        StartCoroutine(RetractJokers());
        float timer = 0;
        while (timer < hideButtonDur)
        {
            yield return null;
            timer += Time.deltaTime;
            Selectables[8].transform.localScale = new Vector3(1, 1, Easing.InExpo(timer, 1f, 0, hideButtonDur));
        }
        Selectables[8].transform.localScale = Vector3.zero;
        timer = 0;
        while (timer < 0.2f)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        float initAlpha = OverlayBG.color.a;
        timer = 0;
        while (timer < fadeInDur)
        {
            yield return null;
            timer += Time.deltaTime;
            OverlayBG.color = new Color(1, 1, 1, Easing.InOutSine(timer, initAlpha, 0, fadeInDur));
        }
        OverlayBG.color = new Color(1, 1, 1, initAlpha);
        OverlayBG.transform.parent.localScale = Vector3.zero;
        OverlayBG.gameObject.SetActive(false);
        for (int i = 0; i < Selectables.Length - 2; i++)
        {
            Selectables[i].transform.localPosition = new Vector3(Selectables[i].transform.localPosition.x, Selectables[i].transform.localPosition.y, -0.08f);
            Selectables[i].GetComponentsInChildren<SpriteRenderer>(true).Where(x => x.name == "Highlight").First().sortingOrder = 13;
        }
        for (int i = 0; i < Selectables.Length - 1; i++)
            Selectables[i].transform.localScale = Vector3.one;
        CannotContinue = false;
        CannotPress = false;
        InEndgame = false;
    }

    private IEnumerator FadeOutText(float duration = 0.3f)
    {
        float timer = 0;
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
            for (int i = 0; i < 3; i++)
                OverlayTexts[i].color = Color.Lerp(Color.white, new Color(1, 1, 1, 0), timer / duration);
        }
        for (int i = 0; i < 3; i++)
            OverlayTexts[i].gameObject.SetActive(false);
    }

    private IEnumerator RetractJokers(float duration = 0.25f)
    {
        var initPositions = new List<Vector3>();
        for (int i = 0; i < JokerRends.Length; i++)
            initPositions.Add(JokerRends[i].transform.parent.localPosition);
        var initScale = JokerRends[0].transform.localScale;
        float timer = 0;
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
            for (int i = 0; i < JokerRends.Length; i++)
            {
                var scale = Easing.OutSine(timer, initScale.x, 0, duration);
                var position = Easing.OutSine(timer, initPositions[i].x, 0, duration);
                JokerRends[i].transform.localScale = Vector3.one * scale;
                JokerRends[i].transform.parent.localPosition = new Vector3(position, JokerRends[i].transform.parent.localPosition.y, JokerRends[i].transform.parent.localPosition.z);
            }
        }
        for (int i = 0; i < JokerRends.Length; i++)
        {
            JokerRends[i].transform.parent.gameObject.SetActive(false);
            JokerRends[i].transform.localScale = initScale;
            JokerRends[i].transform.parent.localPosition = initPositions[i];
        }
    }

    private IEnumerator Endgame(float showTextBoxDur = 0.2f, float pause = 1f)
    {
        InEndgame = true;
        NoMoreMoves.transform.localScale = new Vector3(1, 0, 1);
        foreach (var text in OverlayTexts)
            text.gameObject.SetActive(false);
        foreach (var rend in JokerRends)
            rend.transform.parent.gameObject.SetActive(false);
        NoMoreMoves.SetActive(true);
        float timer = 0;
        while (timer < showTextBoxDur)
        {
            yield return null;
            timer += Time.deltaTime;
            NoMoreMoves.transform.localScale = new Vector3(1, 1, Easing.OutExpo(timer, 0, 1f, showTextBoxDur));
        }
        NoMoreMoves.transform.localScale = Vector3.one;
        timer = 0;
        while (timer < pause)
        {
            yield return null;
            timer += Time.deltaTime;
        }

        StartCoroutine(ResetTable());
        if (!NoBonuses)
        {
            StartCoroutine(TrackReadiness());
            StartCoroutine(ShowStats());
        }

        timer = 0;
        while (timer < showTextBoxDur)
        {
            yield return null;
            timer += Time.deltaTime;
            NoMoreMoves.transform.localScale = new Vector3(1, 1, Easing.InExpo(timer, 1f, 0, showTextBoxDur));
        }
        NoMoreMoves.transform.localScale = Vector3.zero;
        NoMoreMoves.SetActive(false);
    }

    private IEnumerator ShowStats(float fadeInDur = 0.5f, float typeInterval = 0.05f, float textRevealDur = 0.3f, float pause = 0.3f, float jokerSpawnDur = 0.25f)
    {
        var reward = RemainingCards <= 1 ? 3 : RemainingCards <= 3 ? 2 : RemainingCards <= 8 ? 1 : 0;
        JokerCount += reward;
        Debug.LogFormat("[Golf Solitaire #{0}] You finished this round with {1} cards left, so {2} Joker{3} been added to the deck. There are now {4} Joker{5} in play.", _moduleID, RemainingCards.ToString(), reward, reward == 1 ? " has" : "s have", JokerCount, JokerCount == 1 ? "" : "s");
        float targetAlpha = OverlayBG.color.a;
        OverlayBG.color = Color.clear;
        OverlayBG.transform.parent.localScale = Vector3.one;
        OverlayBG.gameObject.SetActive(true);
        float timer = 0;
        while (timer < fadeInDur)
        {
            yield return null;
            timer += Time.deltaTime;
            OverlayBG.color = new Color(1, 1, 1, Easing.InOutSine(timer, 0, targetAlpha, fadeInDur));
        }
        OverlayBG.color = Color.white * new Color(1, 1, 1, targetAlpha);
        for (int i = 0; i < 3; i++)
        {
            OverlayTexts[i].color = Color.clear;
            if (i == 1)
                OverlayTexts[i].text = RemainingCards.ToString();
            OverlayTexts[i].gameObject.SetActive(true);
            timer = 0;
            while (timer < textRevealDur)
            {
                yield return null;
                timer += Time.deltaTime;
                OverlayTexts[i].color = Color.Lerp(new Color(1, 1, 1, 0), Color.white, timer / textRevealDur);
            }
            OverlayTexts[i].color = Color.white;
            timer = 0;
            while (timer < pause)
            {
                yield return null;
                timer += Time.deltaTime;
            }
        }
        var initPositions = new List<Vector3>();
        var initScale = JokerRends[0].transform.localScale;
        for (int i = 0; i < JokerRends.Length; i++)
        {
            initPositions.Add(JokerRends[i].transform.parent.localPosition);
            if (i < reward)
                JokerRends[i].color = Color.white;
            else
                JokerRends[i].color = new Color(1 / 4f, 1 / 4f, 1 / 4f);
            JokerRends[i].transform.localScale = Vector3.zero;
            JokerRends[i].transform.parent.gameObject.SetActive(true);
        }
        timer = 0;
        while (timer < jokerSpawnDur)
        {
            yield return null;
            timer += Time.deltaTime;
            for (int i = 0; i < JokerRends.Length; i++)
            {
                var scale = Easing.InSine(timer, 0, initScale.x, jokerSpawnDur);
                var position = Easing.InSine(timer, 0, initPositions[i].x, jokerSpawnDur);
                JokerRends[i].transform.localScale = Vector3.one * scale;
                JokerRends[i].transform.parent.localPosition = new Vector3(position, JokerRends[i].transform.parent.localPosition.y, JokerRends[i].transform.parent.localPosition.z);
            }
        }
        for (int i = 0; i < JokerRends.Length; i++)
        {
            JokerRends[i].transform.localScale = Vector3.one * initScale.x;
            JokerRends[i].transform.parent.localPosition = initPositions[i];
        }
        ReadyToContinue++;
    }

    private IEnumerator TrackReadiness(float showButtonDur = 0.2f)
    {
        while (ReadyToContinue != 2)
            yield return null;
        CannotContinue = true;
        float timer = 0;
        while (timer < showButtonDur)
        {
            yield return null;
            timer += Time.deltaTime;
            Selectables[8].transform.localScale = new Vector3(1, 1, Easing.OutExpo(timer, 0, 1f, showButtonDur));
        }
        Selectables[8].transform.localScale = Vector3.one;
        CannotContinue = false;
        CannotPress = false;
    }

    private IEnumerator ResetTable(float interval = 0.01f, float shuffleDur = 0.25f)
    {
        float timer = 0;
        for (int i = 4; i >= 0; i--)
        {
            for (int j = 6; j >= 0; j--)
            {
                if (CardRendsOnTheTable[(i * 7) + j] != null)
                    StartCoroutine(CollectCardFromPiles(CardRendsOnTheTable[(i * 7) + j].transform));
                timer = 0;
                while (timer < interval)
                {
                    yield return null;
                    timer += Time.deltaTime;
                }
            }
            timer = 0;
            while (timer < interval)
            {
                yield return null;
                timer += Time.deltaTime;
            }
        }
        StartCoroutine(CollectCardFromPiles(StockCard.transform));
        timer = 0;
        while (timer < 0.2f)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        var shuffleCard = SpawnCard();
        shuffleCard.sortingOrder = -10;
        Destroy(shuffleCard.GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().gameObject);
        shuffleCard.transform.localPosition = DeckRend.transform.localPosition;
        shuffleCard.gameObject.SetActive(true);
        Audio.PlaySoundAtTransform("shuffle", DeckRend.transform);
        timer = 0;
        int dir = (Rnd.Range(0, 2) * 2) - 1;
        while (timer < shuffleDur)
        {
            yield return null;
            timer += Time.deltaTime;
            shuffleCard.transform.localEulerAngles = new Vector3(90, 0, Easing.InOutSine(timer, 0, 180 * dir, shuffleDur));
        }
        Destroy(shuffleCard.gameObject);
        timer = 0;
        while (timer < 0.2f)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        Initialise();
        StartCoroutine(IntroAnim(true));
    }

    private IEnumerator CollectCardFromPiles(Transform target, float duration = 0.15f)
    {
        Audio.PlaySoundAtTransform("deal " + Rnd.Range(1, 4), target);
        var from = target.localPosition;
        var to = DeckRend.transform.localPosition;
        float timer = 0;
        while (timer < duration / 2)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(from, Vector3.Lerp(from, to, 1 / 2f), timer / (duration / 2));
            target.localScale = new Vector3(Easing.InSine(timer, 1, 0, duration / 2), 1, 1);
        }
        target.GetComponent<SpriteRenderer>().sprite = FindCardSprite(-1);
        target.GetComponent<SpriteRenderer>().color = Color.white;
        timer = 0;
        while (timer < duration / 2)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(Vector3.Lerp(from, to, 1 / 2f), to, timer / (duration / 2));
            target.localScale = new Vector3(Easing.InSine(timer, 0, 1, duration / 2), 1, 1);
        }
        DeckRend.transform.localScale = Vector3.one;
        Destroy(target.gameObject);
    }

    private IEnumerator SpinJokers(float oneEightyDur = 1f)
    {
        var cardSprite = -1;
        while (true)
        {
            float timer = 0;
            bool switched = false;
            while (timer < oneEightyDur)
            {
                yield return null;
                timer += Time.deltaTime;
                var scale = Easing.InOutSine(timer, 1, -1, oneEightyDur);
                for (int i = 0; i < JokerRends.Length; i++)
                    JokerRends[i].transform.parent.localScale = new Vector3(Mathf.Abs(scale), 1, 1);
                if (!switched && timer >= oneEightyDur / 2)
                {
                    switched = true;
                    for (int i = 0; i < JokerRends.Length; i++)
                    {
                        JokerRends[i].sprite = FindCardSprite(cardSprite);
                        JokerRends[i].transform.localEulerAngles = new Vector3(90, 0, cardSprite == -1 ? -3 : 3);
                    }
                }
            }
            for (int i = 0; i < JokerRends.Length; i++)
                JokerRends[i].transform.parent.localScale = Vector3.one;
            cardSprite = 51 - cardSprite;
        }
    }

    private IEnumerator PlayCard(int pos, float duration = 0.15f)
    {
        CannotPress = true;
        CardRendsOnTheTable[pos + TableOffsets[pos]].GetComponent<SpriteRenderer>().sortingOrder = 999;
        CardRendsOnTheTable[pos + TableOffsets[pos]].GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().sortingOrder = 998;
        RemainingCards--;
        for (int i = 0; i < Selectables.Length; i++)
            Selectables[i].transform.localScale = Vector3.zero;
        var from = Selectables[pos].transform.localPosition;
        var to = StockCard.transform.localPosition;
        var target = CardRendsOnTheTable[pos + TableOffsets[pos]].transform;
        Audio.PlaySoundAtTransform("deal " + Rnd.Range(1, 4), target);
        var initColour = Color.white;
        if (TableOffsets[pos] != 0)
            initColour = CardRendsOnTheTable[pos + TableOffsets[pos] - 7].color;
        float timer = 0;
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(from, to, timer / duration);
            if (TableOffsets[pos] != 0)
                CardRendsOnTheTable[pos + TableOffsets[pos] - 7].color = Color.Lerp(initColour, Color.white, timer / duration);
        }
        StockCard.sprite = CardRendsOnTheTable[pos + TableOffsets[pos]].sprite;
        StockIx = CardIxsOnTheTable[pos + TableOffsets[pos]];
        Destroy(target.gameObject);
        Selectables[pos].transform.localPosition += new Vector3(0, 0, 0.02f);
        Selectables[pos].GetComponentsInChildren<SpriteRenderer>(true).Where(x => x.name == "Highlight").First().sortingOrder -= 3;
        if (TableOffsets[pos] != 0)
            CardRendsOnTheTable[pos + TableOffsets[pos] - 7].color = Color.white;
        TableOffsets[pos] -= 7;
        if (RemainingCards != 0)
        {
            for (int i = 0; i < Selectables.Length - 2; i++)
                if (TableOffsets[i] >= 0) Selectables[i].transform.localScale = Vector3.one;
            if (!NoMoreCards) Selectables[7].transform.localScale = Vector3.one;
        }
        CheckForStatemate();
    }

    private IEnumerator IntroAnim(bool flagReadiness = false, float interval = 0.01f)
    {
        Debug.LogFormat("[Golf Solitaire #{0}] Started a new round.", _moduleID);
        CardRendsOnTheTable = new List<SpriteRenderer>();
        CardIxsOnTheTable = new List<int>();
        float timer = 0;
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 7; j++)
            {
                CardRendsOnTheTable.Add(SpawnCard());
                CardRendsOnTheTable.Last().sortingOrder = (i * 3) + 2;
                CardRendsOnTheTable.Last().GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().sortingOrder = i * 3;
                CardRendsOnTheTable.Last().gameObject.SetActive(true);
                StartCoroutine(DealCardToPiles(CardRendsOnTheTable.Last().transform, i != 4, DeckRend.transform.localPosition, new Vector3(0.04f * (j - 3), 0, -0.02f * i)));
                timer = 0;
                while (timer < interval)
                {
                    yield return null;
                    timer += Time.deltaTime;
                }
            }
            timer = 0;
            while (timer < interval)
            {
                yield return null;
                timer += Time.deltaTime;
            }
        }
        StockCard = SpawnCard();
        StockCard.sortingOrder = 3;
        StockCard.GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().sortingOrder = 1;
        StockCard.gameObject.SetActive(true);
        StartCoroutine(DealCardToPiles(StockCard.transform, false, DeckRend.transform.localPosition, new Vector3(DeckRend.transform.localPosition.x + 0.04f, DeckRend.transform.localPosition.y, DeckRend.transform.localPosition.z)));
        timer = 0;
        while (timer < 0.2f)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        StockIx = CardIxsOnTheTable.Last();
        CardIxsOnTheTable.RemoveAt(CardIxsOnTheTable.Count() - 1);
        if (flagReadiness)
            ReadyToContinue++;
        if (!flagReadiness || NoBonuses)
        {
            for (int i = 0; i < Selectables.Length - 2; i++)
            {
                Selectables[i].transform.localPosition = new Vector3(Selectables[i].transform.localPosition.x, Selectables[i].transform.localPosition.y, -0.08f);
                Selectables[i].GetComponentsInChildren<SpriteRenderer>(true).Where(x => x.name == "Highlight").First().sortingOrder = 13;
            }
            for (int i = 0; i < Selectables.Length - 1; i++)
                Selectables[i].transform.localScale = Vector3.one;
            CannotPress = false;
        }
    }

    private IEnumerator DealCardToPiles(Transform target, bool darken, Vector3 from, Vector3 to, float duration = 0.15f)
    {
        CardIxsOnTheTable.Add(Deck.Pop());
        var value = CardIxsOnTheTable.Last();
        Audio.PlaySoundAtTransform("deal " + Rnd.Range(1, 4), target);
        target.localPosition = from;
        float timer = 0;
        while (timer < duration / 2)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(from, Vector3.Lerp(from, to, 1 / 2f), timer / (duration / 2));
            target.localScale = new Vector3(Easing.InSine(timer, 1, 0, duration / 2), 1, 1);
        }
        target.GetComponent<SpriteRenderer>().sprite = FindCardSprite(value);
        var lightness = !darken ? 1 : 0.75f;
        target.GetComponent<SpriteRenderer>().color = new Color(lightness, lightness, lightness, 1);
        timer = 0;
        while (timer < duration / 2)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(Vector3.Lerp(from, to, 1 / 2f), to, timer / (duration / 2));
            target.localScale = new Vector3(Easing.OutSine(timer, 0, 1, duration / 2), 1, 1);
        }
        target.localScale = Vector3.one;
        target.localPosition = to;
    }

    private IEnumerator DealCardToStock(float duration = 0.15f)
    {
        CannotPress = true;
        for (int i = 0; i < Selectables.Length; i++)
            Selectables[i].transform.localScale = Vector3.zero;
        var tempStockCard = SpawnCard();
        tempStockCard.transform.localPosition = StockCard.transform.localPosition;
        tempStockCard.sprite = StockCard.sprite;
        tempStockCard.sortingOrder = 0;
        tempStockCard.GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().sortingOrder = -1;
        tempStockCard.gameObject.SetActive(true);
        Destroy(StockCard.gameObject);
        StockCard = SpawnCard();
        StockCard.sortingOrder = 3;
        StockCard.GetComponentsInChildren<SpriteRenderer>().Where(x => x.name == "Shadow").First().sortingOrder = 1;
        StockCard.gameObject.SetActive(true);
        var from = DeckRend.transform.localPosition;
        var to = new Vector3(DeckRend.transform.localPosition.x + 0.04f, DeckRend.transform.localPosition.y, DeckRend.transform.localPosition.z);
        var target = StockCard.transform;
        var value = StockIx = Deck.Pop();
        if (Deck.Count() == 0)
        {
            NoMoreCards = true;
            DeckRend.transform.localScale = Vector3.zero;
        }
        Audio.PlaySoundAtTransform("deal " + Rnd.Range(1, 4), target);
        target.localPosition = from;
        float timer = 0;
        while (timer < duration / 2)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(from, Vector3.Lerp(from, to, 1 / 2f), timer / (duration / 2));
            target.localScale = new Vector3(Mathf.Lerp(1, 0, timer / (duration / 2)), 1, 1);
        }
        target.GetComponent<SpriteRenderer>().sprite = FindCardSprite(value);
        timer = 0;
        while (timer < duration / 2)
        {
            yield return null;
            timer += Time.deltaTime;
            target.localPosition = Vector3.Lerp(Vector3.Lerp(from, to, 1 / 2f), to, timer / (duration / 2));
            target.localScale = new Vector3(Mathf.Lerp(0, 1, timer / (duration / 2)), 1, 1);
        }
        target.localPosition = to;
        Destroy(tempStockCard.gameObject);

        for (int i = 0; i < Selectables.Length - 2; i++)
            if (TableOffsets[i] >= 0) Selectables[i].transform.localScale = Vector3.one;
        if (!NoMoreCards) Selectables[7].transform.localScale = Vector3.one;
        CheckForStatemate();
    }

#pragma warning disable 414
    private string TwitchHelpMessage = "Use '!{0} 12317d' to play a card from columns 1, 2, 3, 1 and 7, then deal a card to the discard pile. You may deal a card without playing, with '!{0} d'. Use '!{0} continue' when a round has finished.";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        command = command.ToLowerInvariant();
        string validcmds = "1234567d";

        if (command == "continue")
        {
            if (ReadyToContinue == 2)
            {
                yield return null;
                Selectables[8].OnInteract();
            }
            else
            {
                yield return "sendtochaterror I'm not ready to continue yet — please wait for the round to end!";
                yield break;
            }
        }
        else
        {
            for (int i = 0; i < command.Length; i++)
                if (!validcmds.Contains(command[i]))
                {
                    yield return "sendtochaterror Invalid command.";
                    yield break;
                }
            if (!CheckTPCommandValidity(command, CardIxsOnTheTable.ToList(), TableOffsets.ToArray(), StockIx))
            {
                yield return "sendtochaterror That series of card plays is impossible!";
                yield break;
            }
            yield return null;
            for (int i = 0; i < command.Length; i++)
            {
                while (CannotPress)
                    yield return null;
                Selectables[validcmds.IndexOf(command[i])].OnInteract();
                if (command[i] == 'd' && i < command.Length - 1)
                {
                    yield return "sendtochat The rest of the command has been cancelled, as a new card has been dealt to the discard pile.";
                    yield break;
                }
            }
        }
    }
    IEnumerator TwitchHandleForcedSolve()
    {
        while (!Solved)
        {
            restart:
            while (CannotPress)
                yield return true;
            if (ReadyToContinue == 2 && !NoBonuses)
            {
                yield return null;
                Selectables[8].OnInteract();
            }
            else
            {
                var possiblePresses = FindSeriesOfPlays(CardIxsOnTheTable.ToList(), TableOffsets.ToArray(), StockIx).OrderBy(x => x.Count()).ToList();
                possiblePresses = possiblePresses.Where(x => x.Count() == possiblePresses.Last().Count()).ToList();
                var presses = possiblePresses.PickRandom();
                Debug.Log(presses.Join(", "));
                foreach (var press in presses)
                {
                    yield return null;
                    while (CannotPress)
                    {
                        yield return null;
                        if (InEndgame)
                            goto restart;
                    }
                    Selectables[press].OnInteract();
                }
            }
        }
    }
}
﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Utilities;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Photon.Pun;

public class Dealer : MonoBehaviourPunCallbacks//, IOnEventCallback
{
    #region Variables
    public static event CommunityCardsUpdate OnCommunityUpdate;
    public static List<Player> bettingPlayers;
    public static Dealer dealerRef;
    public List<Sprite> deckSprites;
    public List<Player> DebugShowBettingPlayers;
    [SerializeField] static bool startMoney = false;

    static Card[] communityCards;
    static List<Card> deck;
    static List<Card> drawnCards;
    static int minimumBet = 5;
    static int currentBetToMatch;
    static int pot = 0;
    static bool finalBettingRound;
    #endregion

    #region Properties
    public static Card[] CommunityCards { get { return communityCards; } }
    public static int HighestBetMade { get { return currentBetToMatch; } }
    public static int Pot { get { return pot; } }
    public static int MinimumBet { get { return minimumBet; } set { minimumBet = value; } }

    #endregion

    #region Startup Methods
    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    private void Awake()
    {
        dealerRef = this;
        finalBettingRound = false;
    }
    public static void SetCardSprite(Card card)
    {
        int indexer;
        if (card.value == CardValue.Ace)
        {
            indexer = (int)card.suit * 13;
            card.sprite = dealerRef.deckSprites[(int)card.suit * 13];
        }

        else
        {
            indexer = (int)card.suit * 13 + (int)card.value - 1;
            card.sprite = dealerRef.deckSprites[(int)card.suit * 13 + (int)card.value - 1];
        }

    }
    static void BuildDeck()
    {
        deck = new List<Card>();
        drawnCards = new List<Card>();
        communityCards = new Card[5];
        CardValue value;
        CardSuit suit;
        Card newCard;
        for (int i = 0; i < 4; i++)
        {
            suit = (CardSuit)i;
            for (int j = 2; j < 15; j++)
            {
                value = (CardValue)j;
                newCard = ScriptableObject.CreateInstance<Card>();
                newCard.name = value + " of " + suit;
                newCard.value = value;
                newCard.suit = suit;
                SetCardSprite(newCard);
                deck.Add(newCard);
            }
        }
        //Debug.Log("Built deck of " + cards.Count + " cards");
    }
    static void ShuffleDeck()
    {
        Card temp;
        int randomIndex;
        for (int i = 0; i < deck.Count; i++)
        {
            randomIndex = Random.Range(0, deck.Count);
            temp = deck[randomIndex];
            deck[randomIndex] = deck[i];
            deck[i] = temp;
        }
        AudioManager.PlayShuffle();
    }
    static void DealCards()
    {
        for (int i = 0; i < 2; i++)
        {
            foreach (Player p in PhotonGameManager.players)
            {
                p.Draw();
                dealerRef.UpdateNetworkPlayers(p, i);
                //AudioManager.PlayCardPull();
            }
        }
    }
    public static void StartGame()
    {
        startMoney = true;
        bettingPlayers = new List<Player>();
        bettingPlayers.AddRange(PhotonGameManager.players);
        BuildDeck();
        ShuffleDeck();
        DealCards();
    }
    #endregion

    #region Main Gameplay
    public static Card Pull()
    {
        Card drawnCard = deck[0];
        drawnCards.Add(drawnCard);
        deck.RemoveAt(0);
        return drawnCard;
    }
    public static void CommunityPull()
    {
        if (communityCards[0] == null)
        {
            Debug.LogWarning("Flop betting round!");
            communityCards[0] = Pull();
            communityCards[1] = Pull();
            communityCards[2] = Pull();

            if (OnCommunityUpdate != null)
                OnCommunityUpdate(3);

            dealerRef.StartCoroutine(WaitForCommunityAnimation());
        }
        else if (communityCards[3] == null)
        {
            Debug.LogWarning("Turn betting round!");
            communityCards[3] = Pull();
            if (OnCommunityUpdate != null)
                OnCommunityUpdate(1);

            dealerRef.StartCoroutine(dealerRef.BettingRound());
        }
        else
        {
            Debug.LogWarning("River betting round! Last Round!");
            finalBettingRound = true;
            communityCards[4] = Pull();
            if (OnCommunityUpdate != null)
                OnCommunityUpdate(1);

            dealerRef.StartCoroutine(WaitForCommunityAnimation());
        }
    }
    public static void StartBettingRound()
    {
        finalBettingRound = false;
        pot = minimumBet * bettingPlayers.Count;
        currentBetToMatch = minimumBet;
        UpdateClientDealer();
        //ParsePlayersCanStillBet();
        //ResetPlayerActions();
        //dealerRef.DebugShowBettingPlayers = bettingPlayers;
        foreach (Player player in bettingPlayers)
        {
            if (startMoney)
                player.money = minimumBet * 100;
            player.OpeningBet();
        }

        if (startMoney)
            UpdateAllClientPlayersMoney(minimumBet * 99, minimumBet);

        UIManager.instance.UpdatePot();
        Debug.LogWarning("First betting round!");
        dealerRef.StartCoroutine(dealerRef.BettingRound());
    }
    public void ResetTable()
    {
        startMoney = true;
        deck.AddRange(drawnCards);
        InstructPlayerToDisposeCards();
        System.Array.Clear(communityCards, 0, 5);
        drawnCards.Clear();
        if (OnCommunityUpdate != null)
            OnCommunityUpdate(-1);

        ShuffleDeck();
        DealCards();

        PhotonGameManager.CurrentPlayer = PhotonGameManager.players[0];
        foreach (Player player in PhotonGameManager.players)
        {
            player.hasChosenAction = false;
            player.playStatus = PlayStatus.Betting;
            player.SetupHand();
        }
        ParsePlayersStillBetting();

        StartBettingRound();
    }
    public static void StartNextRound()
    {
        startMoney = false;
        deck.AddRange(drawnCards);
        InstructPlayerToDisposeCards();
        ParsePlayersCanStillBet();
        ResetPlayerActions();
        System.Array.Clear(communityCards, 0, 5);
        drawnCards.Clear();
        if (OnCommunityUpdate != null)
            OnCommunityUpdate(-1);

        ShuffleDeck();
        DealCards();
        StartBettingRound();
    }
    public static void AddBet(int bet)
    {
        pot += bet;

        //PhotonGameManager.CurrentPlayer.totalAmountBetThisRound = bet;
        currentBetToMatch = currentBetToMatch > PhotonGameManager.CurrentPlayer.TotalBetThisRound ? currentBetToMatch : PhotonGameManager.CurrentPlayer.TotalBetThisRound;
        UpdateClientDealer();
        UIManager.instance.UpdatePot();
        Debug.Log("Highest bet is now: " + currentBetToMatch);
    }
    IEnumerator BettingRound()
    {
        ResetPlayerActions();
        ParsePlayersStillBetting();
        UpdateClientDealer();
        while (!AllPlayersDoneBetting() && bettingPlayers.Count > 1)
        {
            ResetPlayerActions();

            foreach (Player player in bettingPlayers)
            {
                if (player.playStatus == PlayStatus.AllIn)
                {
                    //Debug.Log(player.name + "is all in and cannot bet any more.");
                    continue;
                }

                PhotonGameManager.CurrentPlayer = player;
                PhotonGameManager.CurrentPlayer.PlayerTurnUpdate();
                PhotonGameManager.CurrentPlayer.playerSeat.ShowPlayerTurnMarker();
                Debug.Log(player.name + "'s turn: ");

                while (!player.hasChosenAction)
                {
                    //Debug.Log("Stuck in a loop?");
                    yield return null;
                }
                player.hasChosenAction = false;
                PhotonGameManager.CurrentPlayer.playerSeat.HidePlayerTurnMarker();
                ParsePlayersStillBetting();
                if (bettingPlayers.Count == 1 || AllPlayersDoneBetting())
                    break;
            }

            yield return null;
        }

        if (bettingPlayers.Count == 1 || finalBettingRound)
        {
            PhotonGameManager.DeclareWinner(bettingPlayers);
        }
        else
        {
            CommunityPull();
        }
    }
    #endregion

    #region Helper Methods
    void ParsePlayersStillBetting()
    {
        List<Player> playersStillInGame = new List<Player>();
        foreach (Player p in bettingPlayers)
        {
            if (p.playStatus != PlayStatus.Folded)
                playersStillInGame.Add(p);
            else
            {
                p.playerSeat.GreyOutIcon();
            }
        }
        bettingPlayers = playersStillInGame;
    }
    static void ParsePlayersCanStillBet()
    {
        List<Player> playersNotBroke = new List<Player>();
        foreach (Player p in PhotonGameManager.players)
        {
            if (p.money != 0)
                playersNotBroke.Add(p);
        }
        bettingPlayers = playersNotBroke;
    }
    static void ResetPlayerActions()
    {
        foreach (Player p in bettingPlayers)
        {
            p.hasChosenAction = false;
            p.playStatus = PlayStatus.Betting;
            p.playerSeat.BrightenIcon();
        }
    }
    bool AllPlayersDoneBetting()
    {
        foreach (Player p in bettingPlayers)
        {
            if (p.playStatus == PlayStatus.AllIn)
                continue;

            /*if (p.TotalBetThisRound < currentBetToMatch)
            {
                Debug.Log(p.name + " hasn't matched the bet yet");
                return false;
            }*/
            if (p.playStatus == PlayStatus.Betting)
            {
                Debug.Log(p.name + " is still in the betting status");
                return false;
            }

        }
        return true;
    }

    static IEnumerator WaitForCommunityAnimation()
    {
        while (CommunityHandDisplay.AnimationInProgress)
            yield return null;

        dealerRef.StartCoroutine(dealerRef.BettingRound());
    }
    #endregion

    #region Raise Events
    // Sends player's cards to player's cliet app.
    void UpdateNetworkPlayers(Player player, int cardIndex)
    {
        CardValue tempCardValue = player.cards[cardIndex].value;
        CardSuit tempCardSuit = player.cards[cardIndex].suit;

        object[] datas = new object[] { player.photonView.ViewID, tempCardValue, tempCardSuit };
        RaiseEventOptions raiseEventOptions = new RaiseEventOptions()
        {
            Receivers = ReceiverGroup.Others,
            CachingOption = EventCaching.DoNotCache
        };
        SendOptions sendOptions = new SendOptions() { Reliability = false };

        PhotonNetwork.RaiseEvent((byte)EventCodes.PlayerCards, datas, raiseEventOptions, sendOptions);

    }
    static void InstructPlayerToDisposeCards()
    {
        foreach (Player player in PhotonGameManager.players)
        {
            if (player.cards != null)
                player.cards.Clear();

            //Debug.Log("Dealer: " + player.name + " has cleared cleared his cards");
        }

        object[] datas = new object[] { };
        RaiseEventOptions raiseEventOptions = new RaiseEventOptions()
        {
            Receivers = ReceiverGroup.Others,
            CachingOption = EventCaching.DoNotCache
        };
        SendOptions sendOptions = new SendOptions() { Reliability = false };
        PhotonNetwork.RaiseEvent((byte)EventCodes.ClearPlayerCards, datas, raiseEventOptions, sendOptions);


    }
    static void UpdateAllClientPlayersMoney(int money, int totalBet)
    {
        object[] datas = new object[] { money, totalBet };
        RaiseEventOptions raiseEventOptions = new RaiseEventOptions()
        {
            Receivers = ReceiverGroup.Others,
            CachingOption = EventCaching.DoNotCache
        };
        SendOptions sendOptions = new SendOptions() { Reliability = false };

        PhotonNetwork.RaiseEvent((byte)EventCodes.UpdateAllPlayerMoney, datas, raiseEventOptions, sendOptions);

        if (startMoney)
            startMoney = false;
    }
    static void UpdateClientDealer()
    {
        object[] datas = new object[] { minimumBet, currentBetToMatch, pot };
        RaiseEventOptions raiseEventOptions = new RaiseEventOptions()
        {
            Receivers = ReceiverGroup.Others,
            CachingOption = EventCaching.DoNotCache
        };
        SendOptions sendOptions = new SendOptions() { Reliability = false };

        PhotonNetwork.RaiseEvent((byte)EventCodes.ClientDealer, datas, raiseEventOptions, sendOptions);
    }
    
    /*public static void GiveWinnersEarnings(int[] winnerViewIds)
    {
        object[] datas = new object[] { winnerViewIds, pot/winnerViewIds.Length };
        RaiseEventOptions raiseEventOptions = new RaiseEventOptions()
        {
            Receivers = ReceiverGroup.Others,
            CachingOption = EventCaching.DoNotCache
        };
        SendOptions sendOptions = new SendOptions() { Reliability = false };
        PhotonNetwork.RaiseEvent((byte)EventCodes.GrantWinnerMoney, datas, raiseEventOptions, sendOptions);
    }*/
    /*public void OnEvent(EventData photonEvent)
    {
        byte eventCode = photonEvent.Code;

        switch (eventCode)
        {
            case (byte)EventCodes.PlayerRaise:
                {
                    object[] data = (object[])photonEvent.CustomData;

                    int betToAdd = (int)data[0];
                    PhotonGameManager.CurrentPlayer.Raise(betToAdd);
                    //AddBet(betToAdd);
                    Debug.Log(PhotonGameManager.CurrentPlayer.name + " raised by " + betToAdd);
                }
                break;

            case (byte)EventCodes.PlayerCall:
                {
                    object[] data = (object[])photonEvent.CustomData;

                    //AddBet(betToAdd);
                    PhotonGameManager.CurrentPlayer.Call();
                    Debug.Log(PhotonGameManager.CurrentPlayer.name + " has called");
                }
                break;

            case (byte)EventCodes.PlayerCheck:
                {
                    object[] data = (object[])photonEvent.CustomData;

                    currentBetToMatch = 0;
                    Debug.Log(PhotonGameManager.CurrentPlayer.name + " has checked");

                }
                break;

            case (byte)EventCodes.PlayerFold:
                {
                    object[] data = (object[])photonEvent.CustomData;

                    currentBetToMatch = 0;
                    Debug.Log(PhotonGameManager.CurrentPlayer.name + " has folded");
                }
                break;
        }
    }*/
    #endregion

    
}

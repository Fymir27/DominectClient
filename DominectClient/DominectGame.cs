using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace DominectClient
{
    class Board
    {
        public byte[,] Data { get => data; }
        public uint Width { get => width; }
        public uint Height { get => height; }

        protected Board() { }

        private byte[,] data;
        private uint width;
        private uint height;

        private char myToken;

        private static char esc = (char)0x1B;
        private static string white = esc + "[0m";
        private static string red = esc + "[31m";
        private static string green = esc + "[32m";
        private static string yellow = esc + "[33m";

        private Dictionary<byte, string> rawToDisplayCharacter;

        public void Display(GameTurn myTurn)
        {
            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x == myTurn.X1 && y == myTurn.Y1 || x == myTurn.X2 && y == myTurn.Y2)
                        sb.Append(yellow + myToken);
                    else
                        sb.Append(rawToDisplayCharacter[data[x, y]]);
                }
                sb.AppendLine();
            }
            sb.Append(white);
            Console.WriteLine(sb.ToString());
        }

        public static Board Parse(byte[] raw, uint width, uint height, bool firstPlayer)
        {
            Debug.Assert(raw.Length == width * height);

            var data = new byte[width, height];

            int x = 0;
            int y = 0;
            foreach(var val in raw)
            {
                data[x, y] = val;               
                if(++x >= width)
                {
                    x = 0;
                    y++;
                }
            }

            string firstPlayerMarker = (firstPlayer ? green : red) + "X";
            string secondPlayerMarker = (firstPlayer ? red : green) + "O";

            return new Board() { 
                data = data, 
                width = width, height = height,
                rawToDisplayCharacter = new Dictionary<byte, string>()
                {
                    { (byte)'0', white + '.' },
                    { (byte)'1', firstPlayerMarker },
                    { (byte)'2', secondPlayerMarker }
                },
                myToken= firstPlayer ? 'X' : 'O'
            };
        }
    }
    class DominectGame
    {
        private GameCom.GameComClient client;
        private MatchIDPacket matchID;
        private Random rnd;

        public bool MatchAborted { get; private set; }

        public DominectGame(GameCom.GameComClient client, string matchToken, string userToken)
        {
            this.client = client;
            this.matchID = new MatchIDPacket() { MatchToken = matchToken, UserToken = userToken };
            this.rnd = new Random(); // TODO: Seed?
        }

        public GameStateResponse QueryGameState()
        {
            var cToken = new System.Threading.CancellationToken();
            return client.GetGameState(matchID, null, null, cToken);           
        }

        public void SubmitTurn(GameTurn turn)
        {
            var cToken = new System.Threading.CancellationToken();
            var request = new TurnRequest()
            {
                MatchId = matchID,
                DomGameTurn = turn
            };
            var response = client.SubmitTurn(request, null, null, cToken);
            if(response.TurnStatus == TurnStatus.InvalidTurn)
            {
                Console.WriteLine("Ooops, invalid turn!");
                AbortMatch();
            }

        }

        public void AbortMatch()
        {
            MatchAborted = true;
        }

        public bool GameOver(GameStatus status)
        {
            if (MatchAborted) return true;

            switch (status)
            {
                case GameStatus.MatchWon:
                case GameStatus.MatchLost:
                case GameStatus.Draw:
                case GameStatus.MatchAborted:
                    return true;
            }

            return false;
        }

        public void Start(bool beginningPlayer)
        {
            GameStateResponse gameStateResponse = QueryGameState();
            Console.Write("Waiting for game to start");
            while(gameStateResponse.GameStatus == GameStatus.MatchNotStarted)
            {
                Thread.Sleep(1000);
                Console.Write(".");
                gameStateResponse = QueryGameState();
            }
            Console.WriteLine();
            Console.WriteLine("Opponent found!");

            Console.WriteLine("You are player " + (gameStateResponse.BeginningPlayer ? 1 : 2));
          
            Console.WriteLine("Lets begin!");

            int waitTime = 500;

            bool waitingForOpponent = false;
           
            while(!GameOver(gameStateResponse.GameStatus))
            {
                gameStateResponse = QueryGameState();

                switch (gameStateResponse.GameStatus)
                {
                    case GameStatus.OpponentsTurn:
                        if (waitingForOpponent == false)
                        {
                            waitingForOpponent = true;
                            Console.Write("Waiting for opponent to move");
                        }
                        else
                        {
                            Console.Write(".");
                        }
                        Thread.Sleep(waitTime);
                        break;

                    case GameStatus.YourTurn:
                        Console.WriteLine();
                        waitingForOpponent = false;
                        TakeTurn(gameStateResponse.DomGameState, gameStateResponse.BeginningPlayer);
                        break;                        

                    default:
                        Console.WriteLine();
                        Console.WriteLine("###" + gameStateResponse.GameStatus + "###");
                        break;
                }
            }
        }

        private void TakeTurn(GameState gameState, bool beginningPlayer)
        {
            var board = Board.Parse(gameState.BoardData.ToByteArray(), gameState.BoardWidth, gameState.BoardHeight, beginningPlayer);

            var possibleMoves = new List<GameTurn>();

            for (uint y = 0; y < board.Height; y++)
            {
                for (uint x = 0; x < board.Width; x++)
                {
                    if (board.Data[x, y] != '0') continue;

                    if(x + 1 < board.Width && board.Data[x + 1, y] == '0')
                    {
                        possibleMoves.Add(new GameTurn()
                        {
                            X1 = x,     Y1 = y,
                            X2 = x + 1, Y2 = y
                        }); 
                    }
                    
                    if(y + 1 < board.Height && board.Data[x, y + 1] == '0')
                    {
                        possibleMoves.Add(new GameTurn()
                        {
                            X1 = x, Y1 = y,
                            X2 = x, Y2 = y + 1
                        });
                    }
                }
            }

            if (possibleMoves.Count == 0)
            {
                Console.WriteLine("No moves possible??");
                AbortMatch();
                return;
            }

            var playedMove = possibleMoves[rnd.Next(0, possibleMoves.Count)];
            SubmitTurn(playedMove);
            Console.WriteLine("Move taken: " + playedMove);
            board.Display(playedMove);
            Console.WriteLine("---");
        }
    }
}

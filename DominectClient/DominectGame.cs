using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Linq;

namespace DominectClient
{
    struct Board
    {
        public byte[,] Data { get => data; }
        public uint Width { get => width; }
        public uint Height { get => height; }

        private byte[,] data;
        private uint width;
        private uint height;

        private char myToken;

        private static char esc = (char)0x1B;
        private static string white = esc + "[0m";
        private static string red = esc + "[31m";
        private static string green = esc + "[32m";
        private static string yellow = esc + "[33m";

        public void Display(GameTurn myTurn = null)
        {
            string firstPlayerMarker = (myToken == 'X' ? green : red) + 'X';
            string secondPlayerMarker = (myToken == 'O' ? green : red) + 'O';

            var rawToDisplayCharacter = new Dictionary<byte, string>()
            {
                { (byte)'0', white + '.' },
                { (byte)'1', firstPlayerMarker },
                { (byte)'2', secondPlayerMarker }
            };

            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (myTurn != null && (x == myTurn.X1 && y == myTurn.Y1 || x == myTurn.X2 && y == myTurn.Y2))
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
            foreach (var val in raw)
            {
                data[x, y] = val;
                if (++x >= width)
                {
                    x = 0;
                    y++;
                }
            }

            return new Board()
            {
                data = data,
                width = width,
                height = height,
                myToken = firstPlayer ? 'X' : 'O'
            };
        }

        public static Board DeepCopy(Board other)
        {
            return new Board()
            {
                data = (byte[,])other.data.Clone(),
                width = other.width,
                height = other.height,
                myToken = other.myToken
            };
        }
    }

    public struct Position
    {
        public uint X;
        public uint Y;
        public Position(uint x, uint y)
        {
            X = x;
            Y = y;
        }

        public Position[] Neighbours()
        {
            return new Position[]
            {
                    new Position(X + 1, Y),
                    new Position(X + 1, Y + 1),
                    new Position(X, Y + 1),
                    new Position(X - 1, Y + 1),
                    new Position(X - 1, Y),
                    new Position(X - 1, Y - 1),
                    new Position(X, Y - 1),
                    new Position(X + 1, Y - 1)
            };
        }

        public override bool Equals(object other)
        {
            if (other == null || !(other is Position)) return false;
            var otherPos = (Position)other;
            return this.X == otherPos.X && this.Y == otherPos.Y;
        }

        public override int GetHashCode()
        {
            return ((int)X << 16) + (int)Y;
        }
    }

    class Node
    {
        public int Evaluation;
        public uint X1, Y1, X2, Y2;
        public List<Node> Children = new List<Node>();
        //public Board Board;
        //public int alpha;
        //public int beta;
    }

    class DominectGame
    {
        private GameCom.GameComClient client;
        private MatchIDPacket matchID;
        private Random rnd;        

        public bool MatchAborted { get; private set; }

        Position[] allBoardPositions;
        List<Position>[,] neighbours;

        // TODO: remove after testing
        public DominectGame()
        {
            this.rnd = new Random(); // TODO: Seed?
        }

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
            if (response.TurnStatus == TurnStatus.InvalidTurn)
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
            while (gameStateResponse.GameStatus == GameStatus.MatchNotStarted)
            {
                Thread.Sleep(1000);
                Console.Write(".");
                gameStateResponse = QueryGameState();
            }
            Console.WriteLine();
            Console.WriteLine("Opponent found!");

            Console.WriteLine("Board size: " + gameStateResponse.DomGameState.BoardWidth + "/" + gameStateResponse.DomGameState.BoardHeight);

            Console.WriteLine("You are player " + (gameStateResponse.BeginningPlayer ? 1 : 2));

            Console.WriteLine("Lets begin!");

            
            InitInternals((int)gameStateResponse.DomGameState.BoardWidth, (int)gameStateResponse.DomGameState.BoardHeight);          

            int waitTime = 500;

            bool waitingForOpponent = false;

            while (!GameOver(gameStateResponse.GameStatus))
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
            Console.WriteLine("My turn! Calculating...");
            var start = System.DateTime.Now;
            var board = Board.Parse(gameState.BoardData.ToByteArray(), gameState.BoardWidth, gameState.BoardHeight, beginningPlayer);

            var root = GameTree(board, null, beginningPlayer, int.MinValue, int.MaxValue, 4, 0);

            Node bestChild;

            if (beginningPlayer)
                bestChild = root.Children.Aggregate((best, cur) => cur.Evaluation > best.Evaluation ? cur : best);
            else
                bestChild = root.Children.Aggregate((best, cur) => cur.Evaluation < best.Evaluation ? cur : best);

            var end = System.DateTime.Now;

            Console.WriteLine("Seconds taken for turn: " + (end - start).TotalSeconds);

            var bestMove = new GameTurn()
            {
                X1 = bestChild.X1,
                Y1 = bestChild.Y1,
                X2 = bestChild.X2,
                Y2 = bestChild.Y2,
            };
            SubmitTurn(bestMove);
            Console.WriteLine("Move taken (Eval: " + bestChild.Evaluation + "): ");
            board.Display(bestMove);
            Console.WriteLine("---");
        }

        public static List<GameTurn> GetPossibleMoves(Board board)
        {
            var possibleMoves = new List<GameTurn>();

            for (uint y = 0; y < board.Height; y++)
            {
                for (uint x = 0; x < board.Width; x++)
                {
                    if (board.Data[x, y] != '0') continue;

                    if (x + 1 < board.Width && board.Data[x + 1, y] == '0')
                    {
                        possibleMoves.Add(new GameTurn()
                        {
                            X1 = x,
                            Y1 = y,
                            X2 = x + 1,
                            Y2 = y
                        });
                    }

                    if (y + 1 < board.Height && board.Data[x, y + 1] == '0')
                    {
                        possibleMoves.Add(new GameTurn()
                        {
                            X1 = x,
                            Y1 = y,
                            X2 = x,
                            Y2 = y + 1
                        });
                    }
                }
            }

            return possibleMoves;
        }

        public void InitInternals(int width, int height)
        {
            allBoardPositions = new Position[width * height];
            neighbours = new List<Position>[width, height];
            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    var pos = allBoardPositions[y * width + x];
                    pos.X = x;
                    pos.Y = y;

                    neighbours[x, y] = new List<Position>();
                    foreach (var neighbour in pos.Neighbours())
                    {
                        if (neighbour.X >= 0 &&
                            neighbour.Y >= 0 &&
                            neighbour.X < width &&                            
                            neighbour.Y < height)
                        {
                            neighbours[x, y].Add(neighbour);
                        }
                    }
                }
            }
        }

        public Node GameTree(Board oldBoard, GameTurn move, bool maximizer, int alpha, int beta, int remainingDepth, int depth)
        {           

            Board board;
            if (move != null)
            {
                byte b = maximizer ? (byte)'2' : (byte)'1';
                board = Board.DeepCopy(oldBoard);
                board.Data[move.X1, move.Y1] = b;
                board.Data[move.X2, move.Y2] = b;
            }
            else
            {
                board = oldBoard;
            }

            var curNode = new Node();
            if (move != null)
            {
                curNode.X1 = move.X1;
                curNode.Y1 = move.Y1;
                curNode.X2 = move.X2;
                curNode.Y2 = move.Y2;
            }

            //board.Display();
            //curNode.Board = board;

            if (GameOver(board))
            {
                if (move == null) maximizer = !maximizer;
                //board.Display();                
                curNode.Evaluation = (maximizer ? int.MinValue : int.MaxValue) - (maximizer ? (-depth) : (depth));
                //Console.WriteLine("^Game Over^ -> " + curNode.Evaluation);
                return curNode;
            }

            if (remainingDepth == 0)
            {
                curNode.Evaluation = Heuristic(board, depth);               
                return curNode;
            }

            var possibleMoves = GetPossibleMoves(board);

            if (possibleMoves.Count == 0)
            {
                curNode.Evaluation = 0;
                return curNode;
            }

            foreach (var newMove in possibleMoves)
            {
                var child = GameTree(board, newMove, !maximizer, alpha, beta, remainingDepth - 1, depth + 1);
                curNode.Children.Add(child);
               
                /*
                if (Math.Abs(child.Evaluation) > (int.MaxValue >> 1))
                {
                    curNode.Children.Clear();
                    curNode.Children.Add(child);
                    return curNode;
                }
                */

                if (maximizer)
                {
                    alpha = Math.Max(alpha, child.Evaluation);
                }
                else
                {
                    beta = Math.Min(beta, child.Evaluation);
                }
                
                //curNode.alpha = alpha;
                //curNode.beta = beta;

                if (beta <= alpha)
                {
                    curNode.Evaluation = maximizer ? beta : alpha; ;
                    return curNode;
                }
            }

            curNode.Evaluation = maximizer ? alpha : beta;
            return curNode;
        }

        Dictionary<byte, List<HashSet<Position>>> GetConnectedAreas(Board board)
        {

            var placedMarkers = new Dictionary<byte, HashSet<Position>>()
            {
                { (byte)'1', new HashSet<Position>() },
                { (byte)'2', new HashSet<Position>() }
            };

            for (uint y = 0; y < board.Height; y++)
            {
                for (uint x = 0; x < board.Width; x++)
                {
                    if (board.Data[x, y] == (byte)'0') continue;
                    placedMarkers[board.Data[x, y]].Add(new Position(x, y));
                }
            }

            var result = new Dictionary<byte, List<HashSet<Position>>>()
            {
                { (byte)'1', new List<HashSet<Position>>() },
                { (byte)'2', new List<HashSet<Position>>() }
            };

            foreach (var tuple in placedMarkers)
            {
                var marker = tuple.Key;
                var positions = tuple.Value;

                var queue = new Queue<Position>();
                while (positions.Count > 0)
                {
                    queue.Clear();
                    queue.Enqueue(positions.First());

                    var area = new HashSet<Position>();

                    while (queue.Count > 0)
                    {
                        var curPos = queue.Dequeue();
                        area.Add(curPos);
                        positions.Remove(curPos);
                        var neighbours = curPos.Neighbours();
                        for (int i = 0; i < 8; i++)
                        {
                            var neighbour = neighbours[i];
                            if (positions.Contains(neighbour))
                                queue.Enqueue(neighbour);
                        }
                    }

                    result[marker].Add(area);
                }
            }

            return result;
        }

        public uint AreaSize(HashSet<Position> area, Func<Position, uint> getCoordinate)
        {
            uint min = uint.MaxValue; ;
            uint max = 0;

            foreach (var pos in area)
            {
                uint relevant = getCoordinate(pos);
                if (relevant > max) max = relevant;
                if (relevant < min) min = relevant;
            }

            return max - min + 1;
        }

        public uint heuristicID = 0;
        public int Heuristic(Board board, int depth)
        {
            int[] scores = new int[2];
            int scoreIndex = 0;
            var queue = new Queue<Position>();
            Span<bool> processed = stackalloc bool[(int)board.Width * (int)board.Height];
            foreach (byte b in new byte[] { (byte)'1', (byte)'2' })
            {
                int maxScore = 0;
                processed.Clear();
                foreach (var startingPos in allBoardPositions)
                {
                    int posInex = (int)startingPos.Y * (int)board.Width + (int)startingPos.X;
                    if (processed[posInex]) continue;
                    if (board.Data[startingPos.X, startingPos.Y] != b) continue;
                    queue.Clear();
                    queue.Enqueue(startingPos);
                    processed[posInex] = true;
                    bool player1 = b == (byte)'1';
                    uint minCoord = player1 ? startingPos.X : startingPos.Y;
                    uint maxCoord = minCoord;
                    uint minCoordTotal = uint.MaxValue;
                    uint maxCoordTotal = 0;
                    int size = 0;
                    while (queue.Count > 0)
                    {
                        var pos = queue.Dequeue();
                        foreach (var neighbour in neighbours[pos.X, pos.Y])
                        {
                            int neighbourIndex = (int)neighbour.Y * (int)board.Width + (int)neighbour.X;
                            
                            if (processed[neighbourIndex])
                                continue;

                            processed[neighbourIndex] = true;

                            uint curCoord = player1 ? neighbour.X : neighbour.Y;

                            if(curCoord < minCoordTotal)
                            {
                                minCoordTotal = curCoord;
                            } 
                            else if(curCoord > maxCoordTotal)
                            {
                                maxCoordTotal = curCoord;
                            }

                            byte neighbourByte = board.Data[neighbour.X, neighbour.Y];
                            if (neighbourByte == (byte)'0' && 
                                neighbours[neighbour.X, neighbour.Y]
                                    .Any(p => board.Data[p.X, p.Y] == (byte)'0'))
                            {
                                queue.Enqueue(neighbour);
                            }
                            else if (neighbourByte == b)
                            {
                                queue.Enqueue(neighbour);                                
                                if (curCoord < minCoord)
                                {
                                    size++;
                                    minCoord = curCoord;
                                }
                                else if (curCoord > maxCoord)
                                {
                                    size++;
                                    maxCoord = curCoord;
                                }
                            }
                        }
                    }

                    int score;
                    uint totalSize = maxCoordTotal - minCoordTotal + 1;
                    if(totalSize < (player1 ? board.Width : board.Height))
                    {
                        score = -1;
                    }
                    else
                    {
                        score = size;
                    }
                     
                    maxScore = Math.Max(maxScore, score);
                }

                scores[scoreIndex++] = (int)maxScore - depth;
            }

            var eval = scores[0] - scores[1];
            //Console.WriteLine("^Eval^: " + eval);

            return eval;
        }

        public bool GameOver(Board board)
        {
            var NextYs = new HashSet<int>();
            for (int y = 0; y < board.Height; y++)
            {
                if (board.Data[0, y] == (byte)'1')
                {
                    NextYs.Add(y);
                }
            }

            HashSet<int> curYs;

            bool player1win = true;
            for (int x = 0; x < board.Width - 1; x++)
            {
                curYs = NextYs;
                NextYs = new HashSet<int>();
                foreach (var y in curYs)
                {
                    if (y + 1 < board.Height && board.Data[x + 1, y + 1] == (byte)'1')
                    {
                        if (!NextYs.Contains(y + 1))
                            NextYs.Add(y + 1);
                    }
                    if (board.Data[x + 1, y] == (byte)'1')
                    {
                        if (!NextYs.Contains(y))
                            NextYs.Add(y);
                    }
                    if (y - 1 >= 0 && board.Data[x + 1, y - 1] == (byte)'1')
                    {
                        if (!NextYs.Contains(y - 1))
                            NextYs.Add(y - 1);
                    }
                }

                if (NextYs.Count == 0)
                {
                    player1win = false;
                    break;
                }
            }

            if (player1win) return true;


            var NextXs = new HashSet<int>();
            for (int x = 0; x < board.Width; x++)
            {
                if (board.Data[x, 0] == (byte)'2')
                {
                    NextXs.Add(x);
                }
            }

            HashSet<int> curXs;

            for (int y = 0; y < board.Height - 1; y++)
            {
                curXs = NextXs;
                NextXs = new HashSet<int>();
                foreach (var x in curXs)
                {
                    if (x + 1 < board.Width && board.Data[x + 1, y + 1] == (byte)'2')
                    {
                        if (!NextXs.Contains(x + 1))
                            NextXs.Add(x + 1);
                    }
                    if (board.Data[x, y + 1] == (byte)'2')
                    {
                        if (!NextXs.Contains(x))
                            NextXs.Add(x);
                    }
                    if (x - 1 >= 0 && board.Data[x - 1, y + 1] == (byte)'2')
                    {
                        if (!NextXs.Contains(x - 1))
                            NextXs.Add(x - 1);
                    }
                }

                if (NextXs.Count == 0)
                {
                    return false;
                }
            }

            return true;
        }

    }
}

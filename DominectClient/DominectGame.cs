using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DominectClient
{
    struct Board
    {
        public byte[,] Data;
        public uint Width;
        public uint Height;

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
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (myTurn != null && (x == myTurn.X1 && y == myTurn.Y1 || x == myTurn.X2 && y == myTurn.Y2))
                        sb.Append(yellow + myToken);
                    else
                        sb.Append(rawToDisplayCharacter[Data[x, y]]);
                }
                sb.AppendLine();
            }
            sb.Append(white);
            Console.WriteLine(sb.ToString());
        }

        public static Board Parse(byte[] raw, uint width, uint height, bool firstPlayer)
        {
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
                Data = data,
                Width = width,
                Height = height,
                myToken = firstPlayer ? 'X' : 'O'
            };
        }

        public static Board DeepCopy(Board other)
        {
            return new Board()
            {
                Data = (byte[,])other.Data.Clone(),
                Width = other.Width,
                Height = other.Height,
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

        public Position[] DirectNeighbours()
        {
            return new Position[]
            {
                    new Position(X + 1, Y),
                    new Position(X, Y + 1),
                    new Position(X - 1, Y),
                    new Position(X, Y - 1),
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
        //public List<Node> Children = new List<Node>();
        //public Board Board;
        //public int alpha;
        //public int beta;

        public Node(int eval, uint x1, uint y1, uint x2, uint y2)
        {
            Evaluation = eval;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public Node(int eval, GameTurn move)
        {
            Evaluation = eval;
            if (move == null) return;
            X1 = move.X1;
            Y1 = move.Y1;
            X2 = move.X2;
            Y2 = move.Y2;
        }
    }

    class DominectGame
    {
        private GameCom.GameComClient client;
        private MatchIDPacket matchID;
        private Random rnd;

        public List<Node> Children = new List<Node>();
        public bool MatchAborted { get; private set; }

        public GameStatus Status { get; private set; }

        Position[] allBoardPositions;
        List<Position>[,] neighbours;
        List<Position>[,] directNeighbours;

        public int[] maxPlayerScore = new int[] { 0, 0 };

        Stopwatch stopwatch = new Stopwatch();

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

            stopwatch.Restart();

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
                            //System.GC.Collect();
                            //System.GC.WaitForPendingFinalizers();
                            //System.GC.Collect();
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
                        try
                        {
                            TakeTurn(gameStateResponse.DomGameState, gameStateResponse.BeginningPlayer);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            return;
                        }

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

            Console.WriteLine("Opponent played:");
            board.Display();

            int depth = 4;

            /*
            int tilesToWin = Math.Min((int)board.Width - maxPlayerScore[0], (int)board.Height - maxPlayerScore[1]);
            if(tilesToWin <= 6)
            {
                depth = 3;
            }
            */


            int boardFillAmount = gameState.BoardData.Count(b => b != (byte)'0');
            int boardSize = gameState.BoardData.Length;
            int tilesLeft = boardSize - boardFillAmount;

            Console.WriteLine("My turn! Calculating... Depth " + depth);

            Children.Clear();
            var root = GameTree(board, null, beginningPlayer, int.MinValue, int.MaxValue, depth, 0);

            Node bestChild = null;

            if (beginningPlayer)
                bestChild = Children.Aggregate((best, cur) => cur.Evaluation > best.Evaluation ? cur : best);
            else
                bestChild = Children.Aggregate((best, cur) => cur.Evaluation < best.Evaluation ? cur : best);

            var end = System.DateTime.Now;

            Console.WriteLine("Seconds taken for turn: " + stopwatch.Elapsed.TotalSeconds);

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

        public IEnumerable<GameTurn> GetPossibleMoves(Board board)
        {
            for (uint y = 0; y < board.Height; y++)
            {
                for (uint x = 0; x < board.Width; x++)
                {
                    if (board.Data[x, y] != '0') continue;

                    if (x + 1 < board.Width && board.Data[x + 1, y] == '0')
                    {
                        yield return new GameTurn()
                        {
                            X1 = x,
                            Y1 = y,
                            X2 = x + 1,
                            Y2 = y
                        };
                    }

                    if (y + 1 < board.Height && board.Data[x, y + 1] == '0')
                    {
                        yield return new GameTurn()
                        {
                            X1 = x,
                            Y1 = y,
                            X2 = x,
                            Y2 = y + 1
                        };
                    }
                }
            }
        }

        public void InitInternals(int width, int height)
        {
            allBoardPositions = new Position[width * height];
            neighbours = new List<Position>[width, height];
            directNeighbours = new List<Position>[width, height];
            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    var pos = allBoardPositions[y * width + x];
                    pos.X = x;
                    pos.Y = y;

                    neighbours[x, y] = new List<Position>();
                    directNeighbours[x, y] = new List<Position>();
                    foreach (var neighbour in pos.Neighbours()) //.OrderBy(p => rnd.Next()))
                    {
                        if (neighbour.X >= 0 &&
                            neighbour.Y >= 0 &&
                            neighbour.X < width &&
                            neighbour.Y < height)
                        {
                            neighbours[x, y].Add(neighbour);
                        }
                    }
                    foreach (var neighbour in pos.DirectNeighbours()) //.OrderBy(p => rnd.Next()))
                    {
                        if (neighbour.X >= 0 &&
                            neighbour.Y >= 0 &&
                            neighbour.X < width &&
                            neighbour.Y < height)
                        {
                            directNeighbours[x, y].Add(neighbour);
                        }
                    }
                }
            }
            //allBoardPositions = allBoardPositions.OrderBy(p => rnd.Next()).ToArray();           
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

            //board.Display();
            //curNode.Board = board;

            if (GameOver(board))
            {
                if (move == null) maximizer = !maximizer;
                //board.Display();                
                var eval = (maximizer ? int.MinValue : int.MaxValue) - (maximizer ? (-depth) : (depth));
                //Console.WriteLine("^Game Over^ -> " + curNode.Evaluation);
                return new Node(eval, move);
            }

            if (remainingDepth == 0 || stopwatch.Elapsed.TotalSeconds > 300)
            {
                return new Node(Heuristic(board, depth), move);
            }

            var possibleMoves = GetPossibleMoves(board);

            if (!possibleMoves.Any())
            {
                return new Node(0, move);
            }

            var curNode = new Node(0, move);

            foreach (var newMove in possibleMoves)
            {
                var child = GameTree(board, newMove, !maximizer, alpha, beta, remainingDepth - 1, depth + 1);

                if (depth == 0)
                    Children.Add(child);

                if (maximizer)
                {
                    alpha = Math.Max(alpha, child.Evaluation);
                }
                else
                {
                    beta = Math.Min(beta, child.Evaluation);
                }

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

            System.Threading.Tasks.Parallel.ForEach(new byte[] { (byte)'1', (byte)'2' }, b =>
            {
                int maxScore = 0;
                bool player1 = b == (byte)'1';
                var queue = new Queue<Position>();
                Span<bool> processed = stackalloc bool[(int)board.Width * (int)board.Height];
                foreach (var startingPos in allBoardPositions)
                {
                    int posIndex = (int)startingPos.Y * (int)board.Width + (int)startingPos.X;
                    if (processed[posIndex]) 
                        continue;
                    processed[posIndex] = true;
                    if (board.Data[startingPos.X, startingPos.Y] != b) 
                        continue;
                    queue.Clear();
                    queue.Enqueue(startingPos);
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
                            int neighbourIndex = (int)(neighbour.Y * board.Width + neighbour.X);

                            if (processed[neighbourIndex])
                                continue;

                            processed[neighbourIndex] = true;

                            byte neighbourByte = board.Data[neighbour.X, neighbour.Y];
                            if (neighbourByte == (player1 ? (byte)'2' : (byte)'1')) continue;

                            uint curCoord = player1 ? neighbour.X : neighbour.Y;

                            if (curCoord < minCoordTotal)
                            {
                                minCoordTotal = curCoord;
                            }
                            else if (curCoord > maxCoordTotal)
                            {
                                maxCoordTotal = curCoord;
                            }

                            if (neighbourByte == b)
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
                            else if (directNeighbours[neighbour.X, neighbour.Y]
                                        .Any(p => board.Data[p.X, p.Y] == (byte)'0'))
                            {
                                queue.Enqueue(neighbour);
                            }

                        }
                    }

                    int score;
                    uint totalSize = maxCoordTotal - minCoordTotal + 1;
                    if (totalSize < (player1 ? board.Width : board.Height))
                    {
                        score = int.MinValue + depth;
                    }
                    else
                    {
                        score = size;
                    }

                    maxScore = Math.Max(maxScore, score);
                }

                scores[player1 ? 0 : 1] = maxScore - depth;
            });

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

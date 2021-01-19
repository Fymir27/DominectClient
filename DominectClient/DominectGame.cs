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
        public int Width;
        public int Height;

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

        public static Board Parse(byte[] raw, int width, int height, bool firstPlayer)
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

        public static Board DeepCopyAndAddMove(Board other, GameTurn move, byte b)
        {
            var newBoard = DeepCopy(other);
            newBoard.Data[move.X1, move.Y1] = b;
            newBoard.Data[move.X2, move.Y2] = b;
            return newBoard;
        }
    }

    public struct Position
    {
        public int X;
        public int Y;
        public Position(int x, int y)
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
            return (X << 16) + Y;
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
        public bool CriticalError;

        Position[] allBoardPositions;
        List<Position>[,] neighbours;
        List<Position>[,] directNeighbours;

        public int[] maxPlayerScore = new int[] { 0, 0 };

        public Stopwatch Stopwatch = new Stopwatch();

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
                Status = gameStateResponse.GameStatus;

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
                        Stopwatch.Restart();
                        try
                        {
                            TakeTurn(gameStateResponse.DomGameState, gameStateResponse.BeginningPlayer);
                        }
                        catch (Exception e)
                        {
                            CriticalError = true;
                            Console.WriteLine(e.Message);
                            Console.WriteLine(e.StackTrace);
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

            var board = Board.Parse(gameState.BoardData.ToByteArray(), (int)gameState.BoardWidth, (int)gameState.BoardHeight, beginningPlayer);

            Console.WriteLine("Opponent played:");
            board.Display();

            int tilesTotal = board.Width * board.Height;
            int tilesPlaced = gameState.BoardData.Count(b => b != (byte)'0');
            int tilesFree = tilesTotal - tilesPlaced;
            int depth = 4;
            if(tilesFree < 70)
            {
                depth++;
            }
            if(tilesFree < 30)
            {
                depth++;
            }


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
            var root = GameTree(board, null, beginningPlayer, int.MinValue, int.MaxValue, depth, 0, true);

            Node bestChild = null;

            if (beginningPlayer)
                bestChild = Children.Aggregate((best, cur) => cur.Evaluation > best.Evaluation ? cur : best);
            else
                bestChild = Children.Aggregate((best, cur) => cur.Evaluation < best.Evaluation ? cur : best);

            var end = System.DateTime.Now;

            Console.WriteLine("Seconds taken for turn: " + Stopwatch.Elapsed.TotalSeconds);

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
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
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

            // TODO: weigh around middle? 
            //allBoardPositions = allBoardPositions.OrderBy(p => rnd.Next()).ToArray();      //allBoardPositions.OrderBy(p => Math.Abs(p.X - p.Y)).ToArray();          
        }

        public Node GameTree(Board oldBoard, GameTurn move, bool maximizer, int alpha, int beta, int remainingDepth, int depth, bool presort)
        {

            Board board;
            if (move != null)
            {
                byte b = maximizer ? (byte)'2' : (byte)'1';
                board = Board.DeepCopyAndAddMove(oldBoard, move, b);
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

            if (remainingDepth == 0 || Stopwatch.Elapsed.TotalSeconds > 300)
            {
                return new Node(Heuristic(board, depth), move);
            }

            IEnumerable<GameTurn> possibleMoves;
            if (presort) 
            {
                // presort children based on shallow tree
                Children.Clear();
                var tmpRoot = GameTree(oldBoard, move, maximizer, int.MinValue, int.MaxValue, 2, 0, false);
                possibleMoves = Children.OrderBy(c => c.Evaluation).Select(c => new GameTurn() { X1 = c.X1, X2 = c.X2, Y1 = c.Y1, Y2 = c.Y2 }).ToArray();
                Children.Clear();
            }
            else
            {
                possibleMoves = GetPossibleMoves(board);
            }

            if (!possibleMoves.Any())
            {
                return new Node(0, move);
            }

            var curNode = new Node(0, move);            

            foreach (var newMove in possibleMoves)
            {
                var child = GameTree(board, newMove, !maximizer, alpha, beta, remainingDepth - 1, depth + 1, false);

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

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
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


        static readonly byte[] playerBytes = { (byte)'1', (byte)'2' };
        static readonly byte zeroByte = (byte)'0';

        public int Heuristic(Board board, int depth)
        {
            return Heuristic2(board, depth);
        }

        public int Heuristic1(Board board, int depth)
        {
            int[] scores = new int[2];

            System.Threading.Tasks.Parallel.ForEach(new byte[] { (byte)'1', (byte)'2' }, b =>
            {
                int maxScore = 0;
                bool player1 = b == (byte)'1';
                var queue = new Queue<Position>();
                Span<bool> processed = stackalloc bool[board.Width * board.Height];
                foreach (var startingPos in allBoardPositions)
                {
                    int posIndex = startingPos.Y * board.Width + startingPos.X;
                    if (processed[posIndex]) 
                        continue;
                    processed[posIndex] = true;
                    if (board.Data[startingPos.X, startingPos.Y] != b) 
                        continue;
                    queue.Clear();
                    queue.Enqueue(startingPos);
                    int minCoord = player1 ? startingPos.X : startingPos.Y;
                    int maxCoord = minCoord;
                    int minCoordTotal = int.MaxValue;
                    int maxCoordTotal = 0;
                    int size = 0;
                    while (queue.Count > 0)
                    {
                        var pos = queue.Dequeue();
                        foreach (var neighbour in neighbours[pos.X, pos.Y])
                        {
                            int neighbourIndex = neighbour.Y * board.Width + neighbour.X;

                            if (processed[neighbourIndex])
                                continue;

                            processed[neighbourIndex] = true;

                            byte neighbourByte = board.Data[neighbour.X, neighbour.Y];
                            if (neighbourByte == (player1 ? (byte)'2' : (byte)'1')) continue;

                            int curCoord = player1 ? neighbour.X : neighbour.Y;

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
                    int totalSize = maxCoordTotal - minCoordTotal + 1;
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
                
        public int Heuristic2(Board board, int depth)
        {
            //board.Display();            
            int[] scores = new int[2] { int.MinValue, int.MinValue };           
            Array.ForEach(new int[] { 0, 1 }, playerIndex =>
            {
                bool player1 = playerIndex == 0;
                int w = board.Width;
                int h = board.Height;
                int lenghtPerpendicular = player1 ? h : w;
                int lengthParallel = player1 ? w : h;

                //var queue = new Queue<Position>();
                Span<Position> queue = stackalloc Position[board.Data.Length];
                int queueFront = 0;
                int queueBack = 0;

                Span<int> distance = stackalloc int[board.Data.Length];
                distance.Fill(int.MaxValue);

                int fingerprint = 0;
                if(player1)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (board.Data[0, y] == playerBytes[playerIndex])
                        {
                            distance[fingerprint] = 0;
                            //queue.Enqueue(new Position(0, y));
                            queue[queueBack].X = 0;
                            queue[queueBack].Y = y;
                            queueBack = (queueBack + 1) % board.Data.Length;
                        }
                        else if (board.Data[0, y] == zeroByte)
                        {
                            distance[fingerprint] = 1;
                            //queue.Enqueue(new Position(0, y));
                            queue[queueBack].X = 0;
                            queue[queueBack].Y = y;
                            queueBack = (queueBack + 1) % board.Data.Length;
                        }
                        fingerprint += w;
                    }
                }
                else
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (board.Data[x, 0] == playerBytes[playerIndex])
                        {
                            distance[x] = 0;
                            //queue.Enqueue(new Position(x, 0));
                            queue[queueBack].X = x;
                            queue[queueBack].Y = 0;
                            queueBack = (queueBack + 1) % board.Data.Length;

                        }
                        else if (board.Data[x, 0] == zeroByte)
                        {
                            distance[x] = 1;
                            //queue.Enqueue(new Position(x, 0));
                            queue[queueBack].X = x;
                            queue[queueBack].Y = 0;
                            queueBack = (queueBack + 1) % board.Data.Length;
                        }
                    }
                }
               
                //Span<bool> processed = stackalloc bool[(int)board.Width * (int)board.Height];               

                while (queueFront != queueBack)
                {
                    //var curPos = queue.Dequeue();
                    var curPos = queue[queueFront];
                    queueFront = (queueFront + 1) % board.Data.Length;

                    var curPosFingerprint = curPos.Y * w + curPos.X;                    
                    var newDistance = distance[curPosFingerprint] + 1;
                    //processed[curPosFingerprint] = true;

                    // TODO: check actual dominoe placement?
                    foreach(var neighbour in neighbours[curPos.X, curPos.Y])
                    {
                        var neighbourByte = board.Data[neighbour.X, neighbour.Y];
                        int neighbourFingerprint = neighbour.Y * w + neighbour.X;
                        if (neighbourByte == playerBytes[playerIndex])
                        {
                            if (distance[neighbourFingerprint] > distance[curPosFingerprint])
                            {
                                distance[neighbourFingerprint] = distance[curPosFingerprint];
                                //queue.Enqueue(neighbour);
                                queue[queueBack].X = neighbour.X;
                                queue[queueBack].Y = neighbour.Y;
                                queueBack = (queueBack + 1) % board.Data.Length;
                            }
                        }
                        else if(neighbourByte == zeroByte)
                        {
                            if(distance[neighbourFingerprint] > newDistance)
                            {
                                distance[neighbourFingerprint] = newDistance;
                                queue[queueBack].X = neighbour.X;
                                queue[queueBack].Y = neighbour.Y;
                                queueBack = (queueBack + 1) % board.Data.Length;
                            }
                        }                       
                    }
                }


                int minDistance = int.MaxValue;
                if(player1)
                {
                    for (int i = w - 1; i < distance.Length; i += w)
                    {
                        minDistance = Math.Min(minDistance, distance[i]);
                    }                    
                }
                else
                {
                    for (int i = w * (h - 1); i < distance.Length; i++)
                    {
                        minDistance = Math.Min(minDistance, distance[i]);
                    }
                }

                scores[playerIndex] = int.MaxValue - minDistance;
            });

            var eval = scores[0] - scores[1];            
            //Console.WriteLine($"^ Heuristic: {eval} ^");
            return eval;
        }

        public bool GameOver(Board board)
        {
            bool gameOver = false;
            Array.ForEach(new int[] { 0, 1 }, playerIndex =>
            {
                bool player1 = playerIndex == 0;
                Span<bool> visited = stackalloc bool[board.Width * board.Height];
                var queue = new Queue<Position>();

                if (player1)
                {
                    int fingerprint = 0;
                    for (int y = 0; y < board.Height; y++)
                    {
                        if (board.Data[0, y] == playerBytes[playerIndex])
                        {
                            queue.Enqueue(new Position(0, y));
                            visited[fingerprint] = true;
                        }
                        fingerprint += board.Width;
                    }
                }
                else
                {
                    for (int x = 0; x < board.Width; x++)
                    {
                        if (board.Data[x, 0] == playerBytes[playerIndex])
                        {
                            queue.Enqueue(new Position(x, 0));
                            visited[x] = true;
                        }
                    }
                }                                 

                while (queue.Count > 0)
                {
                    var curPos = queue.Dequeue();
                    var curPosFingerprint = curPos.Y * board.Width + curPos.X;
                    visited[curPosFingerprint] = true;

                    foreach (var neighbour in neighbours[curPos.X, curPos.Y])
                    {                        
                        var neighbourFingerprint = neighbour.Y * board.Width + neighbour.X;
                        if (visited[neighbourFingerprint]) continue;
                        var neighbourByte = board.Data[neighbour.X, neighbour.Y];
                        if (neighbourByte == playerBytes[playerIndex])
                        {
                            if(player1 && neighbour.X == board.Width - 1 || !player1 && neighbour.Y == board.Height - 1)
                            {
                                gameOver = true;                                
                                return;
                            }                            
                            queue.Enqueue(neighbour);
                        }                        
                    }
                }
            });
            return gameOver;
        }

        /*
        // TODO: fix!
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

                for (int y = 0; y < board.Height; y++)
                {
                    if (NextYs.Contains(y)) continue;
                    if(NextYs.Contains(y + 1) || NextYs.Contains(y - 1))
                    {
                        NextYs.Add(y);
                    }
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

                for (int x = 0; x < board.Width; x++)
                {
                    if (NextXs.Contains(x)) continue;
                    if (NextXs.Contains(x + 1) || NextXs.Contains(x - 1))
                    {
                        NextYs.Add(x);
                    }
                }
            }

            return true;
        }
        */

    }
}

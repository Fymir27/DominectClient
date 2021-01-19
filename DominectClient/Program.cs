using System;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Core;
using DominectClient;
using System.IO;
using System.Linq;
using System.Threading;

namespace DominectClient
{    
    class Program
    {
        static string gameserverIP = "http://129.27.202.46:80";
        static string secret = "dangobar";
        static string fullname = "Oliver Pöckelhofer";
        static string matrNr = "01530296";
        static string email = "oliver.poeckelhofer@student.tugraz.at";

        static AuthPacket auth = new AuthPacket()
        {
            MatrNumber = matrNr,
            Secret = secret
        };

        public static void RegisterMe(GameCom.GameComClient client)
        {   
            var cancellationToken = new System.Threading.CancellationToken();
            cancellationToken.Register(() => Console.WriteLine("Request cancelled!"));
            var reply = client.UserRegistration(new UserRegistrationRequest()
            {
                Fullname = fullname,
                MatrNumber = matrNr,
                Email = email,
                Secret = secret
            }, null, null, cancellationToken);

            switch (reply.ErrorCode)
            {
                case UserRegistrationResponse.Types.ErrorCode.Ok:
                    Console.WriteLine("OK:" + reply.ToString());
                    break;

                default:
                    Console.WriteLine("Error: " + reply.ErrorCode.ToString());
                    break;
            }
        }

        public static void RegisterGroup(GameCom.GameComClient client)
        {            
            var cancellationToken = new System.Threading.CancellationToken();
            cancellationToken.Register(() => Console.WriteLine("Request cancelled!"));
            var reply = client.GroupRegistration(new GroupRegistrationRequest()
            {
               Auth = auth,
               MatrNumber = { matrNr }             
            }, null, null, cancellationToken);

            switch (reply.ErrorCode)
            {
                case GroupRegistrationResponse.Types.ErrorCode.Ok:
                    Console.WriteLine("OK:" + reply.ToString());
                    break;

                default:
                    Console.WriteLine("Error: " + reply.ErrorCode.ToString());
                    break;
            }
        }

        public static SetPseudonymResponse.Types.ErrorCode SetPseudonym(GameCom.GameComClient client, string pseudonym)
        {
            var cancellationToken = new System.Threading.CancellationToken();
            cancellationToken.Register(() => Console.WriteLine("Pseudonym request cancelled!"));
            var request = new SetPseudonymRequest()
            {
                Auth = auth,
                Pseudonym = pseudonym
            };
            var reply = client.SetGroupPseudonym(request, null, null, cancellationToken);

            return reply.ErrorCode;
        }

        public static string GetUserToken(GameCom.GameComClient client)
        {
            var cancellationToken = new System.Threading.CancellationToken();
            cancellationToken.Register(() => Console.WriteLine("Request cancelled!"));
            var reply = client.GetUserToken(auth, null, null, cancellationToken);

            return reply.UserToken;
        }

        public static MatchResponse GetDominectMatch(GameCom.GameComClient client, uint width, uint height, string userToken)
        {
            var cancellationToken = new System.Threading.CancellationToken();              
            cancellationToken.Register(() => Console.WriteLine("Request cancelled!"));
            var mmParam = new MatchmakingParameter();
            mmParam.RandomIsDefault = new Nothing();
           
            var request = new MatchRequest()
            {
                UserToken = userToken,
                GameToken = "dom",
                MatchmakingParameters = mmParam,
                TimeoutSuggestionSeconds = 400,
                DomGameParameters = new GameParameter()
                {
                    BoardWidth = width,
                    BoardHeight = height
                }                
            };           

            var reply = client.NewMatch(request, null, DateTime.UtcNow.AddMinutes(1d), cancellationToken);
            return reply;
        }

        public static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var uri = new Uri(gameserverIP);
            var channel = GrpcChannel.ForAddress(uri);           
            var client = new GameCom.GameComClient(channel);

            //RegisterMe(client);
            //RegisterGroup(client);
            string userToken = File.ReadLines("usertoken.txt").First(); //GetUserToken(client);

            if (userToken.Length == 0)
            {
                throw new Exception("Failed to get user token!");
            }

            Console.WriteLine("UserToken: " + userToken);

            /*
            var error = SetPseudonym(client, "Potion Seller");
            if(error == SetPseudonymResponse.Types.ErrorCode.Ok)
            {
                Console.WriteLine("Pseudonym set!");    
            } 
            else
            {
                Console.WriteLine("Error setting Pseudonym: " + error);
            }
            */

            ManualPlay(11, 11, 4); // TODO: comment out and turn playing on again


            bool playing = true;
            int gamesPlayed = 0;
            int wins = 0;
            int aborted = 0;
            bool manualMode = true;

            while(playing && gamesPlayed < 10)
            {
                var match = GetDominectMatch(client, 10, 10, userToken);

                var game = new DominectGame(client, match.MatchToken, userToken);
                game.Start(match.BeginningPlayer);
                if(game.MatchAborted)
                {
                    aborted++;                                                            
                } 
                else if(game.Status == GameStatus.MatchWon)
                {
                    wins++;
                }
                gamesPlayed++;
                Console.WriteLine("Games played: " + gamesPlayed + " - Wins/Aborted: " + wins + "/" + aborted);

                if (manualMode || game.CriticalError)
                {
                    Console.Write("Continue? (y|n) ");
                    var input = Console.ReadKey();
                    if (input.Key == ConsoleKey.Y)
                    {
                        Console.WriteLine();
                        continue;
                    }
                    else
                    {
                        Console.WriteLine("Goodbye!");
                        break;
                    }
                }
            }
            

            //File.WriteAllText("usertoken.txt", userToken);
        }

        public static void ManualPlay(int width, int height, int depth)
        {
            var rawBoard = new byte[width * height];
            Array.Fill(rawBoard, (byte)'0');
            var startingBoard = Board.Parse(rawBoard, width, height, true);

            var dummyGame = new DominectGame();
            dummyGame.InitInternals((int) width, (int)height);

            string line = "";
            do
            {
                //System.GC.Collect();
                //GC.WaitForPendingFinalizers();
                //System.GC.Collect();

                line = Console.ReadLine();

                if (line.Length == 0) break;

                var coords = line.Split(' ');
                uint x1 = uint.Parse(coords[0]);
                uint y1 = uint.Parse(coords[1]);
                uint x2 = uint.Parse(coords[2]);
                uint y2 = uint.Parse(coords[3]);

                startingBoard.Data[x1, y1] = (byte)'1';
                startingBoard.Data[x2, y2] = (byte)'1';

                var playerMove = new GameTurn
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2
                };

                dummyGame.Stopwatch.Restart();
                dummyGame.Children.Clear();
                var start = System.DateTime.Now;
                var root = dummyGame.GameTree(startingBoard, null, false, int.MinValue, int.MaxValue, depth, 0, true);
                var end = System.DateTime.Now;
                Console.WriteLine("Final root value: " + root.Evaluation + " (" + (end - start).TotalSeconds + "s)");              

                var bestChild = dummyGame.Children.Aggregate((best, cur) => cur.Evaluation < best.Evaluation ? cur : best);
                var bestMove = new GameTurn()
                {
                    X1 = bestChild.X1,
                    Y1 = bestChild.Y1,
                    X2 = bestChild.X2,
                    Y2 = bestChild.Y2,
                };

                startingBoard.Data[bestChild.X1, bestChild.Y1] = (byte)'2';
                startingBoard.Data[bestChild.X2, bestChild.Y2] = (byte)'2';
                //startingBoard.Data[x1, y1] = (byte)'1';
                //startingBoard.Data[x2, y2] = (byte)'1';

                startingBoard.Display(bestMove);
                bestChild = null;
                root = null;                

            } while (line.Length > 0);

        }
    }
}

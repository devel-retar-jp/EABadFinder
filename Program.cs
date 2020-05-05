////////////////////////////////////////////////////////////////////////////////////////////////
/// <summary>
///
///  業者ブロックプログラム
///  
/// 手順
/// １．自分をフォロワーの中でdatespan以内のフォロワーだけ抽出
/// ２．抽出されたフォロワーがフォローしていないアカウントのみ抽出
/// ３．アカウントのユーザIDのみを抽出
///
///
///         製造 : Retar.jp   
///         Ver 1.00  2020/05/05
///
/// </summary>
////////////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using System.Text;
using CoreTweet;                                                //追加してください。（Nugetからパッケージを取得）
//                                                              //Newtonsoft.Json追加してください。
using System.Net;
using System.IO;
using System.Threading;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using System.Linq;

namespace CoreTweetList
{
    //#Define
    static class Constants
    {
        public const bool sgOutConsoleDefault = false;          //コンソール出力
        public const string sgFileNameDefault = "sg.json";      //設定ファイル
        public const string sgTypeOfFriends = "Friends";        //フォロワーとフォロー　Followers / Friends
        public const string sgTypeOfFollowers = "Followers";    //フォロワーとフォロー　Followers / Friends
        public const int sgFollowersLimit = 500;                 //Followers取得の上限
    }

    //設定の定義  同一Dirにsg.jsonを入れましょう
    public class SG_JSON
    {
        //Consumer API keys (API key)
        public string ConsumerKey { get; set; }
        //Consumer API keys (API secret key)
        public string ConsumerSecret { get; set; }
        //Access token & access token secret  (Access token)
        public string AccessToken { get; set; }
        //Access token & access token secret  (Access token secret)
        public string AccessSecret { get; set; }
        //最大取得回数(15回）
        //https://developer.twitter.com/en/docs/accounts-and-users/follow-search-get-users/api-reference/get-friends-list
        public int count { get; set; }
        //取得インターバル 秒単位*1000
        public int sleeptime { get; set; }
        //取得ID数 MAX 5000
        public string parm_count { get; set; }
        //The screen name of the user for whom to return results.
        public string parm_screen_name { get; set; }
        //カーソル位置
        public string parm_cursor { get; set; }
        public string OutFileNameFriendsList { get; set; }
        //出力ファイル
        public string OutFileNameGrayFollowersIds { get; set; }
        //開始がX年以内
        public int datespan { get; set; }
        //コンソール出力制御  false:出さない/true:出す
        public bool OutConsole { get; set; }
    }

    class Program
    {
        //フォローリストの読み込み
        static List<User> FollowersList = new List<User>();
        static List<long> FollowersIdsExcept = new List<long>();
        //設定
        static SG_JSON sgjson = new SG_JSON();
        //自分のフォロワーのフォローしている相手を調査する
        static List<long> F_FollowersIds = new List<long>();
        //頻出ID
        static Dictionary<long, int> C_FollowersIds = new Dictionary<long, int>();

        static void Main(string[] args)
        {
            //設定ファイル
            string fileName = Constants.sgFileNameDefault;

            //起動引数から設定ファイル名を取得
            if (args.Length > 0) { fileName = args[0]; }

            //ファイルの存在チェック
            if (System.IO.File.Exists(fileName))
            {
                //シリアライザ
                DataContractJsonSerializer sgjs = new DataContractJsonSerializer(typeof(SG_JSON));
                //ファイルストリーム・オープン
                FileStream sgfs = new FileStream(fileName, FileMode.Open);
                //JSONオブジェクトに設定
                sgjson = (SG_JSON)sgjs.ReadObject(sgfs);
                //ファイルストリーム・クローズ
                sgfs.Close();
            }
            else
            {
                MessageBox.Show("'" + fileName + "'がありません。異常終了");
                Environment.Exit(0);    //異常終了
            }

            //設定読み込み
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            Console.WriteLine("sgjson.ConsumerKey : {0}", sgjson.ConsumerKey);
            Console.WriteLine("sgjson.ConsumerSecret : {0}", sgjson.ConsumerSecret);
            Console.WriteLine("sgjson.AccessToken : {0}", sgjson.AccessToken);
            Console.WriteLine("sgjson.AccessSecret : {0}", sgjson.AccessSecret);
            Console.WriteLine("sgjson.count : {0}", sgjson.count);
            Console.WriteLine("sgjson.sleeptime : {0}", sgjson.sleeptime);
            Console.WriteLine("sgjson.OutFileNameGrayFollowersIds : {0}", sgjson.OutFileNameGrayFollowersIds);
            Console.WriteLine("sgjson.datespan : {0}", sgjson.datespan);
            Console.WriteLine("sgjson.OutConsole : {0}", sgjson.OutConsole);

            //datespanの条件に合った自分のFollowersを取得
            getMyIds();

            //フォロワーをフォローしている相手を抽出
            getFollowersIds();

            //集計
            getCountIds();

            //出力
            getWriteIds();

            //終了
            Console.WriteLine("処理終了 : キー入力");
            Console.ReadKey();                                      //キー入力
        }

        //出力
        static void getWriteIds()
        {
            //書き込み
            Encoding UTF8Enc = System.Text.Encoding.Unicode;
            //テキストファイルをつくりStreamWriterオブジェクト生成
            using (StreamWriter writer = new StreamWriter(sgjson.OutFileNameGrayFollowersIds, false, UTF8Enc))
            {
                writer.WriteLine("Key,Value");
                foreach (var cf in C_FollowersIds.OrderByDescending(x => x.Value))
                {
                    writer.WriteLine("{0},{1}", cf.Key, cf.Value);
                }
            }
        }

        //集計
        static void getCountIds()
        {
            //重複番号
            var fDist = (from c in F_FollowersIds
                         orderby c
                         select c).Distinct();
            foreach (var f in fDist)
            {
                int fCount = (from x in F_FollowersIds select x).Where(x => x == f).Count();
                Dictionary<long, int> cf = new Dictionary<long, int>();
                C_FollowersIds[f] = (from x in F_FollowersIds select x).Where(x => x == f).Count();
            }
        }

        //フォロワーをフォローしている相手を抽出
        static void getFollowersIds()
        {
            //IDごとにとってくる
            int getlimit = Constants.sgFollowersLimit;
            //foreach (var fi in FollowersIds)
            foreach (var fi in FollowersIdsExcept.Distinct())
            {
                Console.WriteLine("followers/ids :  {0}", fi);

                List<long> FoIds = new List<long>();
                List<long> FrIds = new List<long>();

                //Twitter API接続
                try
                {
                    //認証
                    Tokens tokens = Tokens.Create(sgjson.ConsumerKey, sgjson.ConsumerSecret, sgjson.AccessToken, sgjson.AccessSecret);

                    //パラメータ
                    var parm = new Dictionary<string, object>();            //条件指定用Dictionary
                    parm["count"] = sgjson.parm_count;                      //取得数
                    parm["user_id"] = fi;          //取得したいユーザーID
                    if (sgjson.parm_cursor != "") { parm["cursor"] = sgjson.parm_cursor; }  //設定があればカーソル設定

                    //followers/idsを取得
                    int count = 0;
                    parm["cursor"] = -1;

                    for (; ; )
                    {
                        Cursored<long> fls = tokens.Followers.Ids(parm);
                        foreach (var f in fls)
                        {
                            FoIds.Add(f);
                        }
                        //
                        if (fls.NextCursor == 0)
                        {
                            count = count + fls.Count;
                            Console.WriteLine("followers/ids : 獲得数 : {0}", count);
                            break;
                        }
                        else
                        {
                            //Console.WriteLine(" : 次のカーソル : {0}", fls.NextCursor);
                            parm["cursor"] = fls.NextCursor;            //カーソル設定
                            count = count + fls.Count;

                            Console.WriteLine("followers/ids : 獲得数 : {0}", count);
                            Console.WriteLine("followers/ids : ");
                            getSleep(sgjson.sleeptime);//停止時間
                        }
                    }

                    //friends/idsを取得
                    count = 0;
                    parm["cursor"] = -1;
                    for (; ; )
                    {
                        Cursored<long> fls = tokens.Friends.Ids(parm);
                        foreach (var f in fls)
                        {
                            FrIds.Add(f);
                        }
                        //
                        if (fls.NextCursor == 0)
                        {
                            count = count + fls.Count;
                            Console.WriteLine("friends/ids : 獲得数 : {0}", count);
                            break;
                        }
                        else
                        {
                            parm["cursor"] = fls.NextCursor;            //カーソル設定
                            count = count + fls.Count;

                            Console.WriteLine("friends/ids : 獲得数 : {0}", count);
                            Console.WriteLine("friends/ids : ");
                            getSleep(sgjson.sleeptime);//停止時間
                        }
                    }

                    //共通項目
                    var IdsExcept = FoIds.Except<long>(FrIds).ToList();
                    Console.WriteLine("followers only/ids : {0}", IdsExcept.Count());

                    foreach (var f in IdsExcept)
                    {
                        F_FollowersIds.Add(f);
                    }
                }
                catch (TwitterException e)
                {
                    //CoreTweetエラー。
                    Console.WriteLine("CoreTweet Error : {0}", e.Message);
                    //Console.ReadKey();
                }
                catch (System.Net.WebException e)
                {
                    //インターネット接続エラー。
                    Console.WriteLine("Internet Error : {0}", e.Message);
                    // Console.ReadKey();
                }
                getSleep(sgjson.sleeptime);//停止時間

                //取得上限
                if (getlimit < 0) { break; } else { getlimit--; }
            }
        }

        //自分のFollowersとFriendsを取得
        static void getMyIds()
        {
            //Twitter API接続
            try
            {
                //認証
                Tokens tokens = Tokens.Create(sgjson.ConsumerKey, sgjson.ConsumerSecret, sgjson.AccessToken, sgjson.AccessSecret);

                //パラメータ
                var parm = new Dictionary<string, object>();            //条件指定用Dictionary
                parm["screen_name"] = sgjson.parm_screen_name;          //取得したいユーザーID
                if (sgjson.parm_cursor != "") { parm["cursor"] = sgjson.parm_cursor; }  //設定があればカーソル設定

                //followers/listを取得
                int count = 0;
                parm["cursor"] = -1;
                parm["count"] = sgjson.count;                      //取得数
                for (; ; )
                {
                    Cursored<User> fls = tokens.Followers.List(parm);
                    Console.WriteLine("Id                 ,ScreenName         ,Friends,Followers,CreatedAt                 ,Name");
                    foreach (var f in fls)
                    {
                        FollowersList.Add(f);

                        DateTime tyears = DateTime.Now.AddDays(sgjson.datespan);
                        if (tyears < f.CreatedAt)
                        {
                            FollowersIdsExcept.Add((long)f.Id);

                            Console.WriteLine("{0,-19},{1,-19},{2,-7},{3,-9},{4,-26},{5}"
                                , f.Id
                                , f.ScreenName
                                , f.FriendsCount
                                , f.FollowersCount
                                , f.CreatedAt
                                , f.Name
                                );
                        }
                    }
                    //
                    if (fls.NextCursor == 0)
                    {
                        count = count + fls.Count;
                        Console.WriteLine("followers/list : gets : {0}", count);
                        break;
                    }
                    else
                    {
                        parm["cursor"] = fls.NextCursor;            //カーソル設定
                        count = count + fls.Count;

                        Console.WriteLine("followers/list : gets : {0}", count);
                        Console.WriteLine("followers/list : ");
                        getSleep(sgjson.sleeptime);//停止時間
                    }
                }
            }
            catch (TwitterException e)
            {
                //CoreTweetエラー。
                Console.WriteLine("CoreTweet Error : {0}", e.Message);
            }
            catch (System.Net.WebException e)
            {
                //インターネット接続エラー。
                Console.WriteLine("Internet Error : {0}", e.Message);
            }
        }

        //Sleep
        static void getSleep(int sleep)
        {
            Console.CursorVisible = false;

            char[] bars = { '／', '―', '＼', '｜' };

            int sleeptime = 100;
            int ct = sleep / sleeptime;


            for (int i = 0; i < ct; i++)
            {
                // 回転する棒を表示
                Console.Write(bars[i % 4]);

                // 進むパーセンテージを表示
                int per = ((i + 1) * 100) / ct;
                int sper = (sleep - ((sleep * i) / ct)) / 1000;
                Console.Write(" {0, 3:d0}%", per);
                Console.Write(" : Next to Wait : ");
                Console.Write(bars[i % 4]);
                Console.Write(" : {0, 3:d0}Sec", sper);

                // カーソル位置を初期化
                Console.SetCursorPosition(0, Console.CursorTop);

                // （進行が見えるように）処理を100ミリ秒間休止
                System.Threading.Thread.Sleep(sleeptime);
            }

            Console.CursorVisible = true;
            // カーソル位置を初期化
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.WriteLine("Done.                                      ");

        }
    }
}

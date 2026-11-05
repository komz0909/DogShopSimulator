using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 명예의 전당(2026-11-05). 본편 다섯 시즌을 끝까지 마친 판 하나가 기록 하나이고, 상위 5개만 남는다.
    /// 판이 끝나면 세이브는 지워지지만 기록은 남아야 하므로 세이브와 따로 halloffame.json 에 적는다.
    ///
    /// 순위: 1등을 많이 한 판이 위. 같으면 2등 횟수, 그다음 3등 횟수, 그래도 같으면 다섯 시즌 가게 명성 합계가 큰 판.
    /// 그것까지 같으면 먼저 세운 기록이 위다.
    /// </summary>
    public static class HallOfFame
    {
        public const int Max = 5;

        [Serializable]
        public class Entry
        {
            public int[] ranks = new int[0];      // 시즌 순서대로 도그쇼 순위(-1 = 출전 못함)
            public int[] fames = new int[0];      // 시즌마다 쌓은 가게 명성
            public string[] dogNames = new string[0];
            public int[] breeds = new int[0];
            public int totalFame;
            public string date = "";
            public long stamp;
        }

        [Serializable]
        class Book
        {
            public List<Entry> entries = new List<Entry>();
        }

        static Book book;

        /// <summary>방금 들어온 기록의 자리(0부터). 메인 화면이 그 줄을 반짝인다. 없으면 -1.</summary>
        public static int JustAdded = -1;

        /// <summary>판을 다 마치고 타이틀로 갈 때 켠다. 메인 화면이 바로 명예의 전당을 연다.</summary>
        public static bool OpenOnTitle;

        static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, "halloffame.json"); }
        }

        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                Load();
                return book.entries;
            }
        }

        static void Load()
        {
            if (book != null) return;
            try
            {
                if (File.Exists(FilePath)) book = JsonUtility.FromJson<Book>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HallOfFame] 읽기 실패: " + e.Message);
            }
            if (book == null) book = new Book();
            if (book.entries == null) book.entries = new List<Entry>();
        }

        static void Write()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(book, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HallOfFame] 저장 실패: " + e.Message);
            }
        }

        /// <summary>그 판에서 그 순위를 몇 번 했나.</summary>
        public static int CountOf(Entry e, int rank)
        {
            int n = 0;
            if (e != null && e.ranks != null)
                foreach (int r in e.ranks)
                    if (r == rank) n++;
            return n;
        }

        /// <summary>정렬 기준. 음수면 a 가 위.</summary>
        public static int Compare(Entry a, Entry b)
        {
            for (int rank = 1; rank <= 3; rank++)
            {
                int d = CountOf(b, rank).CompareTo(CountOf(a, rank));
                if (d != 0) return d;
            }
            int f = b.totalFame.CompareTo(a.totalFame);
            if (f != 0) return f;
            return a.stamp.CompareTo(b.stamp);
        }

        /// <summary>이 기록이 들어가면 몇 위가 되는가(1~5). 5위 밖이면 0.</summary>
        public static int PreviewPlace(Entry e)
        {
            Load();
            int place = 1;
            foreach (Entry x in book.entries)
                if (Compare(x, e) <= 0) place++;
            return place <= Max ? place : 0;
        }

        /// <summary>기록을 넣고 상위 5개만 남긴다. 들어간 자리(1~5), 밖이면 0.</summary>
        public static int Add(Entry e)
        {
            if (e == null) return 0;
            Load();
            book.entries.Add(e);
            book.entries.Sort(Compare);
            while (book.entries.Count > Max) book.entries.RemoveAt(book.entries.Count - 1);
            Write();
            int index = book.entries.IndexOf(e);
            JustAdded = index;
            return index >= 0 ? index + 1 : 0;
        }
    }
}

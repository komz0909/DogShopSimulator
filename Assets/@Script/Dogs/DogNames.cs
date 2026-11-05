using System.Text;
using UnityEngine;

namespace DogShop.Dogs
{
    /// <summary>
    /// 강아지 이름 거르기(2026-11-05). 글자(한글, 영어)와 띄어쓰기만, 그리고 게임 글꼴(BMJUA)이 그릴 수 있는 글자만 남긴다.
    /// 글꼴에는 한글이 2,367자뿐이라 드문 글자로 이름을 지으면 이름표에 빈칸으로 보였다.
    /// 글꼴이 그리는 글자 목록은 UI/Resources/FontGlyphs.bytes(기본 다국어 판 65,536자 비트표)로, 에디터에서 글꼴 파일을 읽어 만들었다.
    /// </summary>
    public static class DogNames
    {
        public enum Reject { None, Symbol, Glyph }

        static byte[] glyphs;

        /// <summary>글꼴이 그 글자를 그릴 수 있는가. 표를 못 읽으면 막지 않는다.</summary>
        public static bool Drawable(char c)
        {
            if (glyphs == null)
            {
                TextAsset table = Resources.Load<TextAsset>("FontGlyphs");
                glyphs = table != null ? table.bytes : new byte[0];
            }
            if (glyphs.Length < 8192) return true;
            return (glyphs[c >> 3] & (1 << (c & 7))) != 0;
        }

        /// <summary>쓸 수 없는 글자를 뺀 이름. 뺀 까닭을 reject 로 돌려준다(둘 다면 글꼴 쪽).</summary>
        public static string Clean(string s, out Reject reject)
        {
            reject = Reject.None;
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (!(char.IsLetter(c) || c == ' '))
                {
                    if (reject == Reject.None) reject = Reject.Symbol;
                    continue;
                }
                if (c != ' ' && !Drawable(c))
                {
                    reject = Reject.Glyph;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Clean(string s)
        {
            Reject unused;
            return Clean(s, out unused);
        }

        /// <summary>막았을 때 보여 줄 안내.</summary>
        public static string Message(Reject reject)
        {
            return reject == Reject.Glyph ? "글꼴에 없는 글자라 쓸 수 없다. 다른 글자로 지어 보자" : "한글과 영어만 쓸 수 있다";
        }
    }
}

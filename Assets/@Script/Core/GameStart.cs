using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 메인 화면에서 고른 것을 가게 씬으로 넘긴다.
    ///
    /// 씬을 갈아타면 오브젝트가 전부 사라지므로 <b>정적 필드</b>로 들고 간다.
    /// DontDestroyOnLoad 오브젝트를 하나 더 만드는 방법도 있지만, 넘길 것이
    /// 정수 하나와 불 하나뿐이라 그 무게를 질 이유가 없다.
    ///
    /// 가게 씬을 에디터에서 직접 열어 눌러도 그대로 돌아가야 한다 —
    /// 그때는 기본값(새 게임, 씬에 박아 둔 견종)이 쓰인다.
    /// </summary>
    public static class GameStart
    {
        /// <summary>메인 화면을 거쳐 왔는가. 거치지 않았으면 아래 값들은 의미가 없다.</summary>
        public static bool FromMenu;

        /// <summary>이어하기로 들어왔는가. 참이면 가게 씬이 뜨자마자 세이브를 불러온다.</summary>
        public static bool Continue;

        /// <summary>새 게임에서 고른 견종. 이어하기면 세이브의 견종이 이긴다.</summary>
        public static int BreedIndex;

        /// <summary>
        /// 플레이를 시작할 때마다 지운다.
        ///
        /// 정적 필드는 에디터에서 <b>플레이를 멈춰도 살아남을 수 있다</b> —
        /// "Enter Play Mode Options"로 도메인 리로드를 끄면 그렇다. 지금은 켜져 있지만
        /// 그 설정에 기대면, 한 번 이어하기로 들어갔다 나온 뒤 가게 씬을 직접 눌렀을 때
        /// 30일 무인 측정이 <b>조용히 세이브를 불러와</b> 시작한다. 그건 찾기 어려운 사고다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            FromMenu = false;
            Continue = false;
            BreedIndex = 0;
        }

        public static void NewGame(int breedIndex)
        {
            FromMenu = true;
            Continue = false;
            BreedIndex = breedIndex;
        }

        public static void Resume()
        {
            FromMenu = true;
            Continue = true;
        }
    }
}

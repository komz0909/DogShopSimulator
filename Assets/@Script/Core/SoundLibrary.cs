using System;
using UnityEngine;

namespace DogShop.Core
{
    /// <summary>
    /// 소리 목록. Resources/SoundLibrary 하나만 둔다(<see cref="Sound"/> 가 켜질 때 읽는다).
    /// 칸마다 볼륨을 따로 둔다 — 받아 온 파일마다 크기가 제각각이라 여기서 맞춘다.
    /// </summary>
    [CreateAssetMenu(menuName = "DogShop/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string id;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
        }

        public Entry[] sfx = new Entry[0];
        public Entry[] bgm = new Entry[0];

        public Entry Find(Sfx id) => Find(sfx, id.ToString());
        public Entry Find(Bgm id) => Find(bgm, id.ToString());

        static Entry Find(Entry[] list, string id)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i].id == id) return list[i];
            return default;
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>등급 "영원"의 무지개 색(11-03 사용자 - 등급 색 A안: 초월 우주 남보라, 영원 무지개) - 붙은 UI 그림·글자의 색을 매 프레임 무지개로 흘린다.
    /// 알파는 그대로 둬서(LateUpdate) 연출이 알파를 트윈해도 같이 먹는다. Mix = 흰색과 무지개 사이(카드 바탕은 0.55처럼 옅게).</summary>
    public class RainbowGraphic : MonoBehaviour
    {
        public float Mix = 1f;
        public float Offset;
        private Graphic _graphic;

        public static void Attach(Graphic graphic, ItemGrade grade, float mix = 1f)
        {
            if (graphic == null || !EquipmentData.IsRainbow(grade))
                return;
            var rainbow = graphic.GetComponent<RainbowGraphic>() ?? graphic.gameObject.AddComponent<RainbowGraphic>();
            rainbow.Mix = mix;
            rainbow.Offset = Random.value;
        }

        private void LateUpdate()
        {
            if (_graphic == null)
                _graphic = GetComponent<Graphic>();
            if (_graphic == null)
                return;
            var c = Color.Lerp(Color.white, EquipmentData.Rainbow(Time.unscaledTime * EquipmentData.RainbowSpeed + Offset), Mix);
            c.a = _graphic.color.a;
            _graphic.color = c;
        }
    }
}

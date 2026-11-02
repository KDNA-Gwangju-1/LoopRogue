using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>이펙트 확인용 캡처(11-02, 에디터 테스트 메뉴 전용) - 단계마다 이펙트를 재생하고 secondsPerStage 동안 interval마다 게임 화면을 PNG로 찍는다.
    /// 파일: folder/prefix_00.png, _01.png… (찍기 전에 같은 prefix 파일은 지운다).</summary>
    public class FxCapture : MonoBehaviour
    {
        public static void Run(string folder, string prefix, float secondsPerStage, float interval, params Action[] stages)
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, prefix + "_*.png"))
                File.Delete(old);
            var go = new GameObject("FxCapture");
            go.AddComponent<FxCapture>().StartCoroutine(Capture(go, folder, prefix, secondsPerStage, interval, stages));
        }

        private static IEnumerator Capture(GameObject owner, string folder, string prefix, float secondsPerStage, float interval, Action[] stages)
        {
            var index = 0;
            foreach (var stage in stages)
            {
                stage?.Invoke();
                for (var t = 0f; t < secondsPerStage; t += interval)
                {
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{prefix}_{index++:00}.png"));
                    yield return new WaitForSecondsRealtime(interval);
                }
            }
            Debug.Log($"[테스트] 이펙트 캡처 {index}장 → {folder}");
            Destroy(owner);
        }
    }
}

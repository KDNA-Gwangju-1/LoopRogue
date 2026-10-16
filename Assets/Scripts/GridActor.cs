using UnityEngine;

namespace LoopRogue
{
    /// <summary>격자 위에 존재하는 것(플레이어/적)의 공통 기반 - 격자 좌표를 들고 있다가 실제
    /// Transform 위치로 반영하는 것만 책임진다.</summary>
    public abstract class GridActor : MonoBehaviour
    {
        public Vector2Int GridPos { get; set; }
        public CharacterStats Stats { get; protected set; }
        public GridMap Map { get; set; }

        /// <summary>차지하는 칸 반경 - 0이면 한 칸, 1이면 GridPos를 가운데로 3x3(보스). GridMap이 이 칸들을 전부 점유로 잡는다.</summary>
        public virtual int FootprintRadius => 0;

        /// <summary>몸이 차지하는 칸 범위(GridPos 기준 x·y 둘 다 Min~Max) - 반경 몸이면 -r~r, 4층 슬라임 킹 2x2면 0~1(10-16 사용자).</summary>
        public virtual int FootprintMin => -FootprintRadius;
        public virtual int FootprintMax => FootprintRadius;
        public bool IsLarge => FootprintMin != 0 || FootprintMax != 0;
        /// <summary>GridPos에서 몸 가장자리까지 가장 먼 칸 수 - 몸 둘레 칸을 훑을 때 범위.</summary>
        public int FootprintReach => Mathf.Max(-FootprintMin, FootprintMax);

        /// <summary>그림이 놓일 자리 - 짝수 몸(2x2)은 칸 사이 가운데.</summary>
        public Vector3 FootprintVisualOffset => new Vector3((FootprintMin + FootprintMax) * 0.5f * GridConstants.CellSize, (FootprintMin + FootprintMax) * 0.5f * GridConstants.CellSize, 0f);

        /// <summary>지금 격자 칸의 월드 위치(연출이 잠깐 위치를 흔든 뒤 돌아올 기준점).</summary>
        public Vector3 GridWorldPosition => new Vector3(GridPos.x * GridConstants.CellSize, GridPos.y * GridConstants.CellSize, 0f) + FootprintVisualOffset;

        public void SyncTransform()
        {
            transform.position = GridWorldPosition;
        }
    }
}

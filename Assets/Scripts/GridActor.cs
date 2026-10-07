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

        /// <summary>지금 격자 칸의 월드 위치(연출이 잠깐 위치를 흔든 뒤 돌아올 기준점).</summary>
        public Vector3 GridWorldPosition => new Vector3(GridPos.x * GridConstants.CellSize, GridPos.y * GridConstants.CellSize, 0f);

        public void SyncTransform()
        {
            transform.position = GridWorldPosition;
        }
    }
}

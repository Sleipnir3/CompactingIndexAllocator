using Unity.Collections;
using Unity.Mathematics;
using System;

namespace GameWorld.ECS
{
    /// <summary>
    /// 重力索引池 ( Gravity Pool)
    /// 核心理念：通过最小堆（Min-Heap）确保永远优先填充物理内存中最靠前的“坑位”。
    /// 效果：自发实现内存布局的“逻辑压缩”，最大化 Cache Line 利用率并允许整块 Chunk 跳过。
    /// </summary>
    public struct EcsGravityPool : IDisposable
    {
        // 存储已回收索引的最小堆（物理位置越靠前，索引值越小，优先级越高）
        private NativeList<int> _freeHeap;
        
        // 当前物理内存的最高水位线（当堆为空时，向后开辟新空间）
        private int _highWaterMark;

        public EcsGravityPool(int initialCapacity, Allocator allocator)
        {
            _freeHeap = new NativeList<int>(initialCapacity, allocator);
            _highWaterMark = 0;
        }

        /// <summary>
        /// 唤醒/获取一个物理索引
        /// 逻辑：优先从堆中取出最小的“老坑”，若无老坑则推高水位线分配“新地”。
        /// </summary>
        public int Spawn()
        {
            if (_freeHeap.IsCreated && _freeHeap.Length > 0)
            {
                return PopMin();
            }
            return _highWaterMark++;
        }

        /// <summary>
        /// 回收/归还一个物理索引
        /// 逻辑：将索引压入堆，堆序会自动确保该索引在下次 Spawn 时被优先考虑。
        /// </summary>
        public void Despawn(int index)
        {
            // 只有低于水位线的有效索引才允许归还
            if (index < _highWaterMark)
            {
                PushHeap(index);
            }
        }

        public int GetActiveTotalCapacity() => _highWaterMark;
        public int GetFreeCount() => _freeHeap.Length;

        #region 内部堆算法 (Min-Heap Logic)

        private void PushHeap(int index)
        {
            _freeHeap.Add(index);
            int childIdx = _freeHeap.Length - 1;

            while (childIdx > 0)
            {
                int parentIdx = (childIdx - 1) >> 1;
                if (_freeHeap[childIdx] >= _freeHeap[parentIdx]) break;

                // 交换：小的索引向上浮动
                int temp = _freeHeap[childIdx];
                _freeHeap[childIdx] = _freeHeap[parentIdx];
                _freeHeap[parentIdx] = temp;
                childIdx = parentIdx;
            }
        }

        private int PopMin()
        {
            int minIndex = _freeHeap[0];
            int lastIdx = _freeHeap.Length - 1;

            _freeHeap[0] = _freeHeap[lastIdx];
            _freeHeap.RemoveAt(lastIdx);

            if (_freeHeap.Length > 0)
            {
                int parentIdx = 0;
                while (true)
                {
                    int leftChild = (parentIdx << 1) + 1;
                    int rightChild = (parentIdx << 1) + 2;
                    if (leftChild >= _freeHeap.Length) break;

                    int smallestChild = (rightChild < _freeHeap.Length && _freeHeap[rightChild] < _freeHeap[leftChild]) 
                                        ? rightChild : leftChild;

                    if (_freeHeap[parentIdx] <= _freeHeap[smallestChild]) break;

                    int temp = _freeHeap[parentIdx];
                    _freeHeap[parentIdx] = _freeHeap[smallestChild];
                    _freeHeap[smallestChild] = temp;
                    parentIdx = smallestChild;
                }
            }

            return minIndex;
        }

        #endregion

        public void Dispose()
        {
            if (_freeHeap.IsCreated) _freeHeap.Dispose();
        }
    }
}

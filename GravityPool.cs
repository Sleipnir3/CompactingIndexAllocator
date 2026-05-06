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
        private NativeReference<int> _highWaterMark;

        // 防双重释放与水位线退潮的核心防线：活跃掩码
        private NativeBitArray _activeMask;

        public EcsGravityPool(int maxCapacity, Allocator allocator)
        {
            _freeHeap = new NativeList<int>(maxCapacity, allocator);
            _highWaterMark = new NativeReference<int>(0, allocator);
            _activeMask = new NativeBitArray(maxCapacity, allocator, NativeArrayOptions.ClearMemory);
        }

        /// <summary>
        /// 唤醒/获取一个物理索引
        /// 逻辑：优先从堆中取出最小的“老坑”，同时懒惰清理已退潮的僵尸索引。
        /// </summary>
        public int Spawn()
        {
            while (_freeHeap.IsCreated && _freeHeap.Length > 0)
            {
                int minIndex = PopMin();
                
                // 惰性删除 (Lazy Deletion)：如果弹出的索引由于退潮已经处于或高于水位线，则直接丢弃
                if (minIndex < _highWaterMark.Value)
                {
                    _activeMask.Set(minIndex, true);
                    return minIndex;
                }
            }

            int newIndex = _highWaterMark.Value++;
            _activeMask.Set(newIndex, true);
            return newIndex;
        }

        /// <summary>
        /// 回收/归还一个物理索引
        /// 逻辑：防双重释放 + 边缘退潮 (Watermark Retreat) + 压入堆。
        /// </summary>
        public void Despawn(int index)
        {
            // 只有低于水位线的有效索引才允许归还
            if (index < _highWaterMark.Value)
            {
                if (!_activeMask.IsSet(index))
                {
                    throw new InvalidOperationException($"Double Free Detected! Index {index} is already despawned.");
                }
                
                _activeMask.Set(index, false);

                // 核心：水位线退潮机制
                if (index == _highWaterMark.Value - 1)
                {
                    // 如果正好是水位线边缘的坑位，直接退潮
                    _highWaterMark.Value--;

                    // 连续退潮：如果前面的坑位之前也已经空了，继续回缩水位线
                    while (_highWaterMark.Value > 0 && !_activeMask.IsSet(_highWaterMark.Value - 1))
                    {
                        _highWaterMark.Value--;
                    }
                    
                    // 注意：那些被退潮越过去的闲置索引可能还在 _freeHeap 中（成为了僵尸索引）。
                    // 我们不需要 O(N) 去堆里删它们，Spawn() 里的懒惰清理会解决它们。
                }
                else
                {
                    // 不是边缘坑位，乖乖进入重力池沉降
                    PushHeap(index);
                }
            }
        }

        public int GetActiveTotalCapacity() => _highWaterMark.Value;
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
            if (_highWaterMark.IsCreated) _highWaterMark.Dispose();
            if (_activeMask.IsCreated) _activeMask.Dispose();
        }
    }
}

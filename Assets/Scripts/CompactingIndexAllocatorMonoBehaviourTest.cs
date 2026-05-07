using System;
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using GameWorld.ECS;

/// <summary>
/// CompactingIndexAllocator 的 MonoBehaviour 测试脚本。
/// 将此脚本附加到场景中的任何游戏对象上，运行后即可在控制台查看测试结果。
/// </summary>
public class CompactingIndexAllocatorMonoBehaviourTest : MonoBehaviour
{
    private const int MaxCapacity = 100;

    void Start()
    {
        RunAllTests();
    }

    /// <summary>
    /// 运行所有测试用例
    /// </summary>
    public void RunAllTests()
    {
        Debug.Log("========== 开始 CompactingIndexAllocator 单元测试 ==========");

        RunTest(Spawn_Sequential_ReturnsIncrementalIndices);
        RunTest(Despawn_InnerIndex_PushesToHeapAndReuses_ByMinHeap);
        RunTest(Despawn_EdgeIndex_ReducesHighWaterMark);
        RunTest(Despawn_ContinuousEdgeIndices_CascadingRetreat);
        RunTest(Despawn_DoubleFree_ThrowsException);
        RunTest(Spawn_LazyDeletion_IgnoresInvalidHeapIndices);
        RunTest(StressTest_RandomAccess_MaintainsContiguity);

        Debug.Log("========== 所有测试已结束 ==========");
    }

    private void RunTest(Action testAction)
    {
        try
        {
            testAction.Invoke();
            Debug.Log($"<color=green>[PASS]</color> {testAction.Method.Name}");
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[FAIL]</color> {testAction.Method.Name}\n<b>错误信息:</b> {e.Message}\n{e.StackTrace}");
        }
    }

    private void AssertEqual(int expected, int actual, string message = "")
    {
        if (expected != actual)
        {
            string customMsg = string.IsNullOrEmpty(message) ? "" : $" [{message}]";
            throw new Exception($"断言失败{customMsg}: 期望值 {expected}, 但实际得到 {actual}.");
        }
        
        // if (!string.IsNullOrEmpty(message))
        // {
        //     Debug.Log($"    <color=cyan>[验证通过]</color> {message}");
        // }
    }

    /// <summary>
    /// 断言指定 Action 会抛出特定类型的异常。
    /// </summary>
    /// <typeparam name="T">期望抛出的异常类型。</typeparam>
    /// <param name="action">要执行的代码块。</param>
    /// <param name="message">断言失败时的附加消息。</param>
    private void AssertThrows<T>(Action action, string message = "") where T : Exception
    {
        bool threwExpectedException = false;
        try
        {
            action.Invoke();
        }
        catch (T)
        {
            threwExpectedException = true;
        }
        catch (Exception e)
        {
            string customMsg = string.IsNullOrEmpty(message) ? "" : $" [{message}]";
            throw new Exception($"断言失败{customMsg}: 期望抛出 {typeof(T).Name}, 但抛出 {e.GetType().Name}. 错误信息: {e.Message}");
        }

        if (!threwExpectedException)
        {
            string customMsg = string.IsNullOrEmpty(message) ? "" : $" [{message}]";
            throw new Exception($"断言失败{customMsg}: 期望抛出 {typeof(T).Name}, 但没有抛出任何异常。");
        }
        
        // if (!string.IsNullOrEmpty(message))
        // {
        //     Debug.Log($"    <color=cyan>[验证通过]</color> {message}");
        // }
    }

    #region Test Cases

    private void Spawn_Sequential_ReturnsIncrementalIndices()
    {
        using var allocator = new CompactingIndexAllocator(MaxCapacity, Allocator.Temp);
        AssertEqual(0, allocator.Spawn(), "第一次申请");
        AssertEqual(1, allocator.Spawn(), "第二次申请");
        AssertEqual(2, allocator.Spawn(), "第三次申请");
        AssertEqual(3, allocator.GetActiveTotalCapacity(), "水位线应为 3");
    }

    private void Despawn_InnerIndex_PushesToHeapAndReuses_ByMinHeap()
    {
        using var allocator = new CompactingIndexAllocator(MaxCapacity, Allocator.Temp);
        for(int i = 0; i < 5; i++) allocator.Spawn(); // 0, 1, 2, 3, 4

        allocator.Despawn(3);
        allocator.Despawn(1);

        AssertEqual(5, allocator.GetActiveTotalCapacity(), "水位线应保持不变");
        AssertEqual(2, allocator.GetFreeCount(), "空闲堆中应有 2 个元素");
        AssertEqual(1, allocator.Spawn(), "应复用最小的索引 1");
        AssertEqual(3, allocator.Spawn(), "应复用下一个最小索引 3");
    }

    private void Despawn_EdgeIndex_ReducesHighWaterMark()
    {
        using var allocator = new CompactingIndexAllocator(MaxCapacity, Allocator.Temp);
        for(int i = 0; i < 3; i++) allocator.Spawn(); // 0, 1, 2
        AssertEqual(3, allocator.GetActiveTotalCapacity(), "初始水位线");

        allocator.Despawn(2);

        AssertEqual(2, allocator.GetActiveTotalCapacity(), "水位线应回退");
        AssertEqual(0, allocator.GetFreeCount(), "空闲堆应为空");
        AssertEqual(2, allocator.Spawn(), "应在新的水位线位置上申请");
    }

    private void Despawn_ContinuousEdgeIndices_CascadingRetreat()
    {
        using var allocator = new CompactingIndexAllocator(MaxCapacity, Allocator.Temp);
        for(int i = 0; i < 5; i++) allocator.Spawn(); // 0, 1, 2, 3, 4

        allocator.Despawn(2);
        allocator.Despawn(3);
        AssertEqual(5, allocator.GetActiveTotalCapacity(), "回收内部索引后水位线不变");

        allocator.Despawn(4); 

        AssertEqual(2, allocator.GetActiveTotalCapacity(), "水位线应发生连续回退");
    }

    private void Despawn_DoubleFree_ThrowsException()
    {
        using var allocator = new CompactingIndexAllocator(MaxCapacity, Allocator.Temp);
        allocator.Spawn(); // 0
        allocator.Despawn(0);

        AssertThrows<InvalidOperationException>(() => allocator.Despawn(0), "重复释放索引");
    }
    private void Spawn_LazyDeletion_IgnoresInvalidHeapIndices()
    {
        using var allocator = new CompactingIndexAllocator(MaxCapacity, Allocator.Temp);
        for(int i = 0; i < 3; i++) allocator.Spawn(); // 0, 1, 2

        allocator.Despawn(1);
        allocator.Despawn(2); 
        AssertEqual(1, allocator.GetActiveTotalCapacity(), "水位线回退后");

        AssertEqual(1, allocator.Spawn(), "应忽略堆中无效索引并从新水位线分配");
    }

    private void StressTest_RandomAccess_MaintainsContiguity()
    {
        int operations = 100000;
        using var allocator = new CompactingIndexAllocator(operations, Allocator.Temp);
        var activeIndices = new List<int>(operations);
        var rng = new System.Random(42); 

        // 1. 10w次随机乱序存取
        for (int i = 0; i < operations; i++)
        {
            if (activeIndices.Count == 0 || rng.NextDouble() > 0.5)
            {
                activeIndices.Add(allocator.Spawn());
            }
            else
            {
                int removeAt = rng.Next(activeIndices.Count);
                int indexToDespawn = activeIndices[removeAt];
                
                activeIndices[removeAt] = activeIndices[activeIndices.Count - 1];
                activeIndices.RemoveAt(activeIndices.Count - 1);
                
                allocator.Despawn(indexToDespawn);
            }
        }

        // 2. 检测连续性：算出所有的"碎片"空洞，并重新申请填满它们
        int gaps = allocator.GetActiveTotalCapacity() - activeIndices.Count;
        for (int i = 0; i < gaps; i++)
        {
            activeIndices.Add(allocator.Spawn());
        }
        
        // 填满所有坑位后，活跃索引数必定等于最高水位线（完全紧凑）
        AssertEqual(activeIndices.Count, allocator.GetActiveTotalCapacity(), "暴力测试: 填补空洞后水位线未对齐");

        // 3. 终极验证：把所有数据归还，观察是否能正常级联退潮
        foreach (int idx in activeIndices) allocator.Despawn(idx);
        AssertEqual(0, allocator.GetActiveTotalCapacity(), "暴力测试: 全部清空后水位线未归零");
    }

    #endregion
}
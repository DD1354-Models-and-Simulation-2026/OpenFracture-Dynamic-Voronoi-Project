using System.Diagnostics;
using UnityEngine;


public class Evaluator : MonoBehaviour
{
 private Stopwatch stopwatch;
    private Fracture fracture;

    void Awake()
    {
        fracture = GetComponent<Fracture>();
    }

    public void OnFractureStart(Collider col, GameObject obj, Vector3 point)
    {
        stopwatch = Stopwatch.StartNew();
    }

    public void OnFractureComplete()
    {
        stopwatch.Stop();

        int shardCount = 0;

        // fragmentRoot is private, so we infer it by name
        string rootName = gameObject.name + "Fragments";
        GameObject root = GameObject.Find(rootName);

        if (root != null)
            shardCount = root.transform.childCount;

        UnityEngine.Debug.Log(
            $"Time: {stopwatch.Elapsed.TotalMilliseconds} ms | Shards: {shardCount}");
    }
}
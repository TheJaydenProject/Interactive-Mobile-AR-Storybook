using UnityEngine;
using System.Collections.Generic;

public class PendantManager : MonoBehaviour
{
    private bool _blueSparkCollected;
    private bool _redSparkCollected;
    private bool _yellowSparkCollected;
    private bool _goldSparkCollected;

    public void CollectBlueSpark()
    {
        _blueSparkCollected = true;
        Debug.Log("[PendantManager] Blue Spark collected.");
    }

    public void CollectRedSpark()
    {
        _redSparkCollected = true;
        Debug.Log("[PendantManager] Red Spark collected.");
    }

    public void CollectYellowSpark()
    {
        _yellowSparkCollected = true;
        Debug.Log("[PendantManager] Yellow Spark collected.");
    }

    public void CollectGoldSpark()
    {
        _goldSparkCollected = true;
        Debug.Log("[PendantManager] Gold Spark collected.");
    }

    public List<string> GetCollectedSparks()
    {
        List<string> collectedSparks = new List<string>();
        if (_blueSparkCollected) collectedSparks.Add("Blue");
        if (_redSparkCollected) collectedSparks.Add("Red");
        if (_yellowSparkCollected) collectedSparks.Add("Yellow");
        if (_goldSparkCollected) collectedSparks.Add("Gold");
        return collectedSparks;
    }
}

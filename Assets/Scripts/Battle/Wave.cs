using System;
using UnityEngine;

public class Wave
{
    public Wave(Vector3 origin, int count, FormationType formation, Quaternion initalRotation)
    {
        Origin = origin;
        Count = count;
        Formation = formation;
        InitialRotation = initalRotation;
    }
    public Vector3 Origin { get; }
    public int Count{ get; }
    public FormationType Formation { get; }
    public Quaternion InitialRotation { get; }

    public void GetShipLocation(int index)
    {
        
    }

}

public enum FormationType
{
    Line,Circle
        //, Row, V
}

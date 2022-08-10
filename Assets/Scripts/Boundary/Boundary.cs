using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Boundary : MonoBehaviour {
    [Header("Values")]
    [SerializeField] private int _x;
    [SerializeField] private int _y;
    [SerializeField] private int _z;

    [Header("Objects")]
    [SerializeField] private BoxCollider _top;
    [SerializeField] private BoxCollider _right;
    [SerializeField] private BoxCollider _bottom;
    [SerializeField] private BoxCollider _left;

    [Header("LocalNavMeshBuilder Script")]
    [SerializeField] private LocalNavMeshBuilder _script;

    private void Start() {
        Setup();
    }

    private void Setup() {
        _top.transform.position = new Vector3(0, 0, _z / 2);
        _top.size = new Vector3(_x, _y, 1);

        _right.transform.position = new Vector3(_x / 2, 0, 0);
        _right.size = new Vector3(_z, _y, 1);

        _bottom.transform.position = new Vector3(0, 0, -_z / 2);
        _bottom.size = new Vector3(_x, _y, 1);

        _left.transform.position = new Vector3(-_x / 2, 0, 0);
        _left.size = new Vector3(_z, _y, 1);

        _script.m_Size = new Vector3(_x, _y, _z);
    }

}

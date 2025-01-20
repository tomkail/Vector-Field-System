// using UnityEngine;
// using UnityEditor;
// using UnityEditor.Experimental.GraphView;
// using System.Collections.Generic;
//
// public class ControlPointsGraphView : EditorWindow
// {
//     private GraphView graphView;
//
//     [MenuItem("Window/Control Points Graph")]
//     public static void OpenWindow()
//     {
//         GetWindow<ControlPointsGraphView>("Control Points Graph");
//     }
//
//     private void OnEnable()
//     {
//         graphView = new GraphView
//         {
//             style = { flexGrow = 1 }
//         };
//         rootVisualElement.Add(graphView);
//
//         CreateControlPoints();
//     }
//
//     private void CreateControlPoints()
//     {
//         for (int i = 0; i < 4; i++)
//         {
//             var controlPoint = CreateControlPointNode($"Control Point {i + 1}", new Vector2(100 * (i + 1), 100 * (i + 1)));
//             graphView.AddElement(controlPoint);
//         }
//     }
//
//     private Node CreateControlPointNode(string title, Vector2 position)
//     {
//         var node = new Node
//         {
//             title = title,
//             style = { width = 100, height = 100 }
//         };
//
//         node.SetPosition(new Rect(position, Vector2.zero));
//
//         var inputPort = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
//         inputPort.portName = "In";
//         node.inputContainer.Add(inputPort);
//
//         var outputPort = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
//         outputPort.portName = "Out";
//         node.outputContainer.Add(outputPort);
//
//         node.RefreshPorts();
//         node.RefreshExpandedState();
//
//         return node;
//     }
// }
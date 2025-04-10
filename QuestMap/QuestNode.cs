using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using Lumina.Excel.Sheets;

namespace QuestMap {
    internal class QuestNode {
        private string? _name;

        internal uint Id { get; }
        internal string Name => this._name ??= this.Quest.Name.ExtractText();
        internal List<QuestNode> Parents { get; set; }
        internal Quest Quest { get; private set; }
        internal List<QuestNode> Children { get; } = [];

        internal QuestNode(List<QuestNode> parents, uint id, Quest quest) {
            this.Id = id;
            this.Parents = parents;
            this.Quest = quest;
        }

        public QuestNode(uint id, string? name = null) {
            this.Id = id;
            this._name = name;
            this.Parents = [];
            this.Quest = default!;
        }

        internal QuestNode? Find(uint id) {
            if (this.Id == id) {
                return this;
            }

            foreach (var child in this.Children) {
                var result = child.Find(id);
                if (result != null) {
                    return result;
                }
            }

            return null;
        }

        internal IEnumerable<QuestNode> Ancestors() {
            var parents = new Stack<QuestNode>();
            foreach (var parent in this.Parents) {
                parents.Push(parent);
            }

            while (parents.TryPop(out var next)) {
                yield return next;
                foreach (var parent in next.Parents) {
                    parents.Push(parent);
                }
            }
        }

        internal IEnumerable<QuestNode> Ancestors(Func<QuestNode, QuestNode?> consolidator) {
            var parents = new Stack<QuestNode>();
            foreach (var parent in this.Parents) {
                var consolidated = consolidator(parent);
                if (consolidated is not null)
                {
                    consolidated.Children.Add(consolidated);
                    parents.Push(consolidated);
                }
                else
                {
                    parents.Push(parent);
                }
            }

            while (parents.TryPop(out var next)) {
                yield return next;
                foreach (var parent in next.Parents) {
                    var consolidated = consolidator(parent);
                    if (consolidated is not null)
                    {
                        consolidated.Children.Add(consolidated);
                        parents.Push(consolidated);
                    }
                    else
                    {
                        parents.Push(parent);
                    }
                }
            }
        }

        public struct TraverseEnumerator
        {
            private readonly Stack<QuestNode> _stack = new();
            public QuestNode Current { readonly get; private set; } = null!;

            public TraverseEnumerator(QuestNode start)
            {
                this._stack.Push(start); 
            }

            public bool MoveNext()
            {
                if (this._stack.TryPop(out var node))
                {
                    this.Current = node;
                    var children = node.Children;
                    for (var index = 0; index < children.Count; index++) this._stack.Push(children[index]);
                    return true;
                }
                return false;
            }
        }

        public struct TraverseEnumerable(QuestNode start)
        {
            public readonly TraverseEnumerator GetEnumerator() => new(start);
        }

        internal TraverseEnumerable Traverse() => new(this);

        internal IEnumerable<Tuple<QuestNode, uint>> TraverseWithDepth() {
            var stack = new Stack<Tuple<QuestNode, uint>>();
            stack.Push(Tuple.Create(this, (uint) 0));
            while (stack.TryPop(out var next)) {
                yield return next;
                foreach (var child in next.Item1.Children) {
                    stack.Push(Tuple.Create(child, next.Item2 + 1));
                }
            }
        }

        internal static (List<QuestNode>, Dictionary<uint, QuestNode>) BuildTree(Dictionary<uint, Quest> layouts) {
            var lookup = new Dictionary<uint, QuestNode>();
            var rootNodes = new List<QuestNode>();
            var allNodes = new Dictionary<uint, QuestNode>();

            foreach (var item in layouts) {
                if (lookup.TryGetValue(item.Key, out var ourNode)) {
                    ourNode.Quest = item.Value;
                } else {
                    ourNode = new QuestNode([], item.Key, item.Value);
                    lookup[item.Key] = ourNode;
                    allNodes[item.Key] = ourNode;
                }

                var previous = item.Value.PreviousQuests();
                if (!previous.Any()) {
                    rootNodes.Add(ourNode);
                } else {
                    foreach (var prev in previous) {
                        if (!lookup.TryGetValue(prev.RowId, out var parentNode)) {
                            // create preliminary parent
                            parentNode = new QuestNode(prev.RowId);
                            lookup[prev.RowId] = parentNode;
                            allNodes[prev.RowId] = parentNode;
                        }

                        parentNode.Children.Add(ourNode);
                        ourNode.Parents.Add(parentNode);
                    }
                }
            }

            return (rootNodes, allNodes);
        }
    }

    internal static class NodeExt {
        internal static QuestNode? Find(this IEnumerable<QuestNode> nodes, uint id) {
            foreach (var node in nodes) {
                var found = node.Find(id);

                if (found != null) {
                    return found;
                }
            }

            return null;
        }

        internal static IEnumerable<Quest> PreviousQuests(this Quest quest) {
            foreach (var previous in quest.PreviousQuest) {
                if (previous.IsValid && previous.RowId != 0) {
                    yield return previous.Value!;
                }
            }
        }
    }
}

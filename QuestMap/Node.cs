using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace QuestMap {
    internal class Node<T> {
        internal uint Id { get; }
        internal List<Node<T>> Parents { get; set; }
        internal T Value { get; set; }
        internal List<Node<T>> Children { get; } = [];

        internal Node(List<Node<T>> parents, uint id, T value) {
            this.Id = id;
            this.Parents = parents;
            this.Value = value;
        }

        private Node(uint id) {
            this.Id = id;
            this.Parents = [];
            this.Value = default!;
        }

        internal Node<T>? Find(uint id) {
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

        internal IEnumerable<Node<T>> Ancestors() {
            var parents = new Stack<Node<T>>();
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

        internal IEnumerable<Node<T>> Ancestors(Func<T, T?> consolidator) {
            var parents = new Stack<Node<T>>();
            foreach (var parent in this.Parents) {
                var consolidated = consolidator(parent.Value);
                parents.Push(consolidated == null
                    ? parent
                    : new Node<T>([], parent.Id, consolidated) {
                        Children = { this },
                    });
            }

            while (parents.TryPop(out var next)) {
                yield return next;
                foreach (var parent in next.Parents) {
                    var consolidated = consolidator(parent.Value);
                    parents.Push(consolidated == null
                        ? parent
                        : new Node<T>([], parent.Id, consolidated) {
                            Children = { next },
                        });
                }
            }
        }

        internal IEnumerable<Node<T>> Traverse() {
            var stack = new Stack<Node<T>>();
            stack.Push(this);
            while (stack.TryPop(out var next)) {
                yield return next;
                foreach (var child in next.Children) {
                    stack.Push(child);
                }
            }
        }

        internal IEnumerable<Tuple<Node<T>, uint>> TraverseWithDepth() {
            var stack = new Stack<Tuple<Node<T>, uint>>();
            stack.Push(Tuple.Create(this, (uint) 0));
            while (stack.TryPop(out var next)) {
                yield return next;
                foreach (var child in next.Item1.Children) {
                    stack.Push(Tuple.Create(child, next.Item2 + 1));
                }
            }
        }

        internal static (List<Node<Quest>>, Dictionary<uint, Node<Quest>>) BuildTree(Dictionary<uint, Quest> layouts) {
            var lookup = new Dictionary<uint, Node<Quest>>();
            var rootNodes = new List<Node<Quest>>();
            var allNodes = new Dictionary<uint, Node<Quest>>();

            foreach (var item in layouts) {
                if (lookup.TryGetValue(item.Key, out var ourNode)) {
                    ourNode.Value = item.Value;
                } else {
                    ourNode = new Node<Quest>([], item.Key, item.Value);
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
                            parentNode = new Node<Quest>(prev.RowId);
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
        internal static Node<T>? Find<T>(this IEnumerable<Node<T>> nodes, uint id) {
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

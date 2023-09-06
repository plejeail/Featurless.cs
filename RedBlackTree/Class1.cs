namespace Featurless;

public sealed class RedBlackTree<TKey, TValue>
        where TKey : unmanaged
        where TValue : unmanaged
{
    private struct Node
    {
        private Node*  _leftNode;
        private Node*  _rightNode;
        private TKey   _key;
        private TValue _value;
    }

    void unsafe insert(TKey key, TValue value) {
        Node* currentNode = _root;
        Node* nextNode = currentNode;
        while (nextNode != null) {

        }
    }


    private unsafe Node* _root;
}

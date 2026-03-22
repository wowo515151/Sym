// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Regions;

namespace SymCobra.Core;

public static class CobraInstantiator
{
    public static int Instantiate(CobraGraphState graphState, IExpression template, IReadOnlyDictionary<string, int> bindings)
    {
        if (template is Wild wild)
        {
            if (bindings.TryGetValue(wild.Name, out int classId)) return classId;
            return graphState.Find(graphState.GetNode(graphState.AddNode($"Sym:{wild.Name}", 8, [], graphState.AddClass(), $"Sym:{wild.Name}")).ClassId);
        }

        if (template is Atom atom)
        {
            if (atom is Symbol s && bindings.TryGetValue(s.Name, out int classId)) return classId;
            
            string head = atom.Head;
            int headCode = CobraNodeMatchEncoding.EncodeHeadCode(head);
            string? literal = (headCode == 8 || headCode == 9) ? head : null;
            return graphState.Find(graphState.GetNode(graphState.AddNode(head, headCode, [], graphState.AddClass(), literal)).ClassId);
        }

        if (template is Operation op)
        {
            int[] childrenIds = new int[op.Arguments.Count];
            for (int i = 0; i < op.Arguments.Count; i++)
            {
                childrenIds[i] = Instantiate(graphState, op.Arguments[i], bindings);
            }
            
            string head = ENode.GetHead(op);
            int headCode = CobraNodeMatchEncoding.EncodeHeadCode(head);
            string? literal = (headCode == 8 || headCode == 9) ? head : null;
            
            return graphState.Find(graphState.GetNode(graphState.AddNode(head, headCode, childrenIds, graphState.AddClass(), literal)).ClassId);
        }

        throw new NotSupportedException($"Cannot instantiate expression of type {template.GetType().Name}");
    }
}

package passes

import (
	"fmt"
	"sova/internal/diag"
	"sova/internal/ir"
)

// PassInferTypes is a pass that infers types based on usage and context. It also checks for consistent type usages (type checking).
type PassInferTypes struct{}

func (p *PassInferTypes) Name() string       { return "infer_types" }
func (p *PassInferTypes) Scope() PassScope   { return PerPackage }
func (p *PassInferTypes) Requires() []string { return []string{"resolve_names", "resolve_typerefs"} }
func (p *PassInferTypes) NoErrors() bool     { return false }

func (p *PassInferTypes) Run(pc *PassContext) error {
	for _, f := range pc.Pkg.Files {
		p.resolveStmts(pc, f.Hir.Statements)
	}
	return nil
}

func (p *PassInferTypes) resolveStmts(pc *PassContext, stmts []ir.Stmt) {
	// Check for unreachable code
	for i, st := range stmts {
		if p.isTerminator(st) && i < len(stmts)-1 {
			pc.Diag.Report(diag.ErrUnreachableCode, st.Span())
			break
		}
	}

	for _, st := range stmts {
		switch st := st.(type) {
		case *ir.BlockStmt:
			p.resolveStmts(pc, st.Stmts)
		case *ir.VarDeclStmt:
			if funcLit, ok := st.Init.(*ir.FuncLitExpr); ok && len(st.Targets) == 1 {
				target := &st.Targets[0]
				for _, param := range funcLit.Params {
					if param.Type != nil && param.Type.Typ != 0 {
						pc.Pkg.Syms.SetType(param.Name.Sym, param.Type.Typ)
					} else if param.Default != nil {
						ti := p.synthesizeTypeFromExpr(pc, param.Default)
						pc.Pkg.Syms.SetType(param.Name.Sym, ti)
					} else {
						pc.Pkg.Syms.SetType(param.Name.Sym, pc.Types.TypError())
						pc.Diag.Report(diag.ErrTypeInferenceFailed, param.Name.Span, fmt.Sprintf("parameter '%s'", param.Name.Name))
					}
				}

				funcTyp := pc.Types.FuncOf(funcLit.Params, funcLit.ReturnType.Typ)
				if target.Name != nil {
					pc.Pkg.Syms.SetType(target.Name.Sym, funcTyp)
				}
				funcLit.SetType(funcTyp)

				p.resolveStmts(pc, funcLit.Body.Stmts)

				if target.TypeAnn != nil && target.TypeAnn.Typ != 0 {
					expected := target.TypeAnn.Typ
					if ok, _ := isTypeAssignable(pc.Types, expected, funcTyp); !ok {
						exTy, _ := pc.Types.GetByID(expected)
						funcTy, _ := pc.Types.GetByID(funcTyp)
						pc.Diag.Report(diag.ErrTypeMismatch, st.Span(), exTy.Key, funcTy.Key)
					}
				} else {
					target.TypeAnn = &ir.TypeRef{Typ: funcTyp}
				}
			} else {
				tInit := p.synthesizeTypeFromExpr(pc, st.Init)

				if len(st.Targets) == 1 {
					target := &st.Targets[0]
					if target.TypeAnn != nil && target.TypeAnn.Typ != 0 {

						expected := target.TypeAnn.Typ
						if ok, _ := isTypeAssignable(pc.Types, expected, tInit); !ok {
							exTy, _ := pc.Types.GetByID(expected)
							tInitTy, _ := pc.Types.GetByID(tInit)
							pc.Diag.Report(diag.ErrTypeMismatch, st.Span(), exTy.Key, tInitTy.Key)
						}
						if target.Name != nil {
							pc.Pkg.Syms.SetType(target.Name.Sym, expected)
						}
					} else {
						if target.Name != nil {
							pc.Pkg.Syms.SetType(target.Name.Sym, tInit)
						}
						target.TypeAnn = &ir.TypeRef{Typ: tInit}
					}
				} else {
					tupleTyp, ok := pc.Types.GetByID(tInit)
					if !ok || tupleTyp.Kind != ir.TK_Tuple {
						pc.Diag.Report(diag.ErrTypeMismatch, st.Span(), "tuple", "non-tuple")
					} else {
						if len(st.Targets) != len(tupleTyp.Fields) {
							pc.Diag.Report(diag.ErrTypeMismatch, st.Span(),
								fmt.Sprintf("expected %d values", len(st.Targets)),
								fmt.Sprintf("got %d values", len(tupleTyp.Fields)))
						} else {
							for i, target := range st.Targets {
								fieldTyp := tupleTyp.Fields[i].Type
								if target.Name != nil {
									pc.Pkg.Syms.SetType(target.Name.Sym, fieldTyp)
								}
								if target.TypeAnn == nil {
									st.Targets[i].TypeAnn = &ir.TypeRef{Typ: fieldTyp}
								}
							}
						}
					}
				}
			}
		case *ir.FuncDeclStmt:
			for _, param := range st.Params {
				if param.Type != nil && param.Type.Typ != 0 {
					pc.Pkg.Syms.SetType(param.Name.Sym, param.Type.Typ)
				} else if param.Default != nil {
					ti := p.synthesizeTypeFromExpr(pc, param.Default)
					pc.Pkg.Syms.SetType(param.Name.Sym, ti)
				} else {
					pc.Pkg.Syms.SetType(param.Name.Sym, pc.Types.TypError())
					pc.Diag.Report(diag.ErrTypeInferenceFailed, param.Name.Span, fmt.Sprintf("parameter '%s'", param.Name.Name))
				}
			}

			p.resolveStmts(pc, st.Body.Stmts)

			var returnType ir.TypID
			if st.ReturnType == nil || st.ReturnType.Typ == 0 {
				returnTypes := p.collectReturnTypes(pc, st.Body.Stmts)

				if len(returnTypes) == 0 {
					returnType = pc.Types.TypNone()
				} else {
					var noneReturns []struct {
						Typ  ir.TypID
						Span diag.TextSpan
					}
					var nonNoneReturns []struct {
						Typ  ir.TypID
						Span diag.TextSpan
					}

					for _, rt := range returnTypes {
						if rt.Typ == pc.Types.TypNone() {
							noneReturns = append(noneReturns, rt)
						} else {
							nonNoneReturns = append(nonNoneReturns, rt)
						}
					}

					if len(noneReturns) > 0 && len(nonNoneReturns) > 0 {
						baseType := nonNoneReturns[0].Typ
						for i := 1; i < len(nonNoneReturns); i++ {
							rt := nonNoneReturns[i]
							if ok, _ := isTypeAssignable(pc.Types, baseType, rt.Typ); !ok {
								if ok2, _ := isTypeAssignable(pc.Types, rt.Typ, baseType); ok2 {
									baseType = rt.Typ
								} else {
									pc.Diag.Report(diag.ErrOptionReturnMismatch, rt.Span)
									baseType = pc.Types.TypError()
									break
								}
							}
						}

						if baseType != pc.Types.TypError() {
							returnType = pc.Types.OptionOf(baseType)
						} else {
							returnType = pc.Types.TypError()
						}
					} else if len(noneReturns) > 0 {
						returnType = pc.Types.TypNone()
					} else {
						returnType = nonNoneReturns[0].Typ
						for i := 1; i < len(nonNoneReturns); i++ {
							rt := nonNoneReturns[i]
							if rt.Typ != returnType {
								pc.Diag.Report(diag.ErrTypeMismatch, rt.Span,
									fmt.Sprintf("return type at statement %d", i+1),
									fmt.Sprintf("%s vs %s", typeName(pc, returnType), typeName(pc, rt.Typ)))
							}
						}
					}
				}

				if st.ReturnType == nil {
					st.ReturnType = &ir.TypeRef{Typ: returnType}
				} else {
					st.ReturnType.Typ = returnType
				}
			} else {
				returnType = st.ReturnType.Typ

				returnTypes := p.collectReturnTypes(pc, st.Body.Stmts)
				for _, rt := range returnTypes {
					if ok, _ := isTypeAssignable(pc.Types, returnType, rt.Typ); !ok {
						pc.Diag.Report(diag.ErrTypeMismatch, rt.Span,
							typeName(pc, returnType), typeName(pc, rt.Typ))
					}
				}
			}

			funcTyp := pc.Types.FuncOf(st.Params, returnType)
			pc.Pkg.Syms.SetType(st.Name.Sym, funcTyp)

		case *ir.ExternDeclStmt:
			for _, fn := range st.Funcs {
				var returnType ir.TypID
				if fn.ReturnType != nil && fn.ReturnType.Typ != 0 {
					returnType = fn.ReturnType.Typ
				} else {
					returnType = pc.Types.TypNone()
				}

				funcTyp := pc.Types.FuncOf(fn.Params, returnType)
				pc.Pkg.Syms.SetType(fn.Name.Sym, funcTyp)
			}
			for _, v := range st.Vars {
				if v.Type != nil && v.Type.Typ != 0 {
					pc.Pkg.Syms.SetType(v.Name.Sym, v.Type.Typ)
				} else {
					pc.Pkg.Syms.SetType(v.Name.Sym, pc.Types.TypError())
					pc.Diag.Report(diag.ErrTypeInferenceFailed, v.Name.Span, fmt.Sprintf("extern variable '%s'", v.Name.Name))
				}
			}

		case *ir.EnumDeclStmt:
			// Determine if this is a numeric enum (no payload fields) or a payload enum
			isNumeric := len(st.Fields) == 0

			// Compute ordinals and values
			nextValue := int64(0)
			var caseInfos []ir.EnumCaseInfo

			for _, c := range st.Cases {
				if c.Value != nil {
					nextValue = *c.Value
				}

				caseInfos = append(caseInfos, ir.EnumCaseInfo{
					Name:    c.Name.Name,
					Ordinal: c.Ordinal,
					Value:   nextValue,
				})
				nextValue++

				// Type check case arguments against field types for payload enums
				if !isNumeric {
					for i, arg := range c.Args {
						argType := p.synthesizeTypeFromExpr(pc, arg)
						if i < len(st.Fields) && st.Fields[i].Type != nil {
							fieldType := st.Fields[i].Type.Typ
							if fieldType != 0 {
								if ok, _ := isTypeAssignable(pc.Types, fieldType, argType); !ok {
									fieldTy, _ := pc.Types.GetByID(fieldType)
									argTy, _ := pc.Types.GetByID(argType)
									fieldKey := "unknown"
									argKey := "unknown"
									if fieldTy != nil {
										fieldKey = string(fieldTy.Key)
									}
									if argTy != nil {
										argKey = string(argTy.Key)
									}
									pc.Diag.Report(diag.ErrTypeMismatch, arg.Span(), fieldKey, argKey)
								}
							}
						}
					}
				}
			}

			// Build enum field info
			var enumFields []ir.EnumFieldInfo
			for _, field := range st.Fields {
				fieldType := ir.TypID(0)
				if field.Type != nil {
					fieldType = field.Type.Typ
				}
				enumFields = append(enumFields, ir.EnumFieldInfo{
					Name: field.Name.Name,
					Type: fieldType,
				})
			}

			// Create enum type
			enumTyp := pc.Types.EnumOf(st.Name.Name, caseInfos, enumFields, isNumeric)
			pc.Pkg.Syms.SetType(st.Name.Sym, enumTyp)

			// Set type for each case symbol
			for _, c := range st.Cases {
				pc.Pkg.Syms.SetType(c.Name.Sym, enumTyp)
			}

			// Type check methods and collect method info
			var enumMethods []ir.EnumMethodInfo
			for _, method := range st.Methods {
				// Find and set the type for "this" in the method scope
				methodScope, _ := pc.Pkg.Scopes.EnclosingScope(method.Body.ID())
				if thisSym, found := pc.Pkg.Scopes.Lookup(methodScope, "this"); found {
					pc.Pkg.Syms.SetType(thisSym, enumTyp)
				}

				for _, param := range method.Params {
					if param.Type != nil && param.Type.Typ != 0 {
						pc.Pkg.Syms.SetType(param.Name.Sym, param.Type.Typ)
					} else if param.Default != nil {
						ti := p.synthesizeTypeFromExpr(pc, param.Default)
						pc.Pkg.Syms.SetType(param.Name.Sym, ti)
					} else {
						pc.Pkg.Syms.SetType(param.Name.Sym, pc.Types.TypError())
						pc.Diag.Report(diag.ErrTypeInferenceFailed, param.Name.Span, fmt.Sprintf("parameter '%s'", param.Name.Name))
					}
				}

				p.resolveStmts(pc, method.Body.Stmts)

				var returnType ir.TypID
				if method.ReturnType == nil || method.ReturnType.Typ == 0 {
					returnTypes := p.collectReturnTypes(pc, method.Body.Stmts)
					if len(returnTypes) == 0 {
						returnType = pc.Types.TypNone()
					} else {
						returnType = returnTypes[0].Typ
					}

					if method.ReturnType == nil {
						method.ReturnType = &ir.TypeRef{Typ: returnType}
					} else {
						method.ReturnType.Typ = returnType
					}
				} else {
					returnType = method.ReturnType.Typ
				}

				funcTyp := pc.Types.FuncOf(method.Params, returnType)
				pc.Pkg.Syms.SetType(method.Name.Sym, funcTyp)

				// Collect method info
				methodOrigName := method.Name.Name
				enumMethods = append(enumMethods, ir.EnumMethodInfo{
					Name: methodOrigName,
					Type: funcTyp,
				})
			}

			// Update the enum type with method information
			if enumTy, ok := pc.Types.GetByID(enumTyp); ok {
				enumTy.EnumMethods = enumMethods
			}

		case *ir.ExprStmt:
			p.synthesizeTypeFromExpr(pc, st.Expr)
		case *ir.MultiAssignmentStmt:
			tValue := p.synthesizeTypeFromExpr(pc, st.Value)

			tupleTyp, ok := pc.Types.GetByID(tValue)
			if !ok || tupleTyp.Kind != ir.TK_Tuple {
				pc.Diag.Report(diag.ErrTypeMismatch, st.Span(), "tuple", "non-tuple")
			} else {
				if len(st.Targets) != len(tupleTyp.Fields) {
					pc.Diag.Report(diag.ErrTypeMismatch, st.Span(),
						fmt.Sprintf("expected %d values", len(st.Targets)),
						fmt.Sprintf("got %d values", len(tupleTyp.Fields)))
				}
			}
		case *ir.IfStmt:
			tCond := p.synthesizeTypeFromExpr(pc, st.Cond)
			if tCond != pc.Types.PrimBool() {
				pc.Diag.Report(diag.ErrTypeMismatch, st.Cond.Span(), "bool", typeName(pc, tCond))
			}

			p.resolveStmts(pc, st.Then.Stmts)
			for _, eb := range st.ElseIfs {
				tCond := p.synthesizeTypeFromExpr(pc, eb.Cond)
				if tCond != pc.Types.PrimBool() {
					pc.Diag.Report(diag.ErrTypeMismatch, eb.Cond.Span(), "bool", typeName(pc, tCond))
				}
				p.resolveStmts(pc, eb.Then.Stmts)
			}
			if st.Else != nil {
				p.resolveStmts(pc, st.Else.Stmts)
			}
		case *ir.SwitchStmt:
			if st.Expr != nil {
				p.synthesizeTypeFromExpr(pc, st.Expr)
			}
			for _, cs := range st.Cases {
				for _, ce := range cs.Values {
					p.synthesizeTypeFromExpr(pc, ce)
				}
				p.resolveStmts(pc, cs.Stmts)
			}
			if st.Default != nil {
				p.resolveStmts(pc, st.Default)
			}
		case *ir.ReturnStmt:
			for _, result := range st.Results {
				p.synthesizeTypeFromExpr(pc, result)
			}
		case *ir.GuardStmt:
			tCond := p.synthesizeTypeFromExpr(pc, st.Cond)
			if tCond != pc.Types.PrimBool() && !pc.Types.IsTypeOfKind(tCond, ir.TK_Option) {
				pc.Diag.Report(diag.ErrTypeMismatch, st.Cond.Span(), "bool or option<T>", typeName(pc, tCond))
			}

			if pc.Types.IsTypeOfKind(tCond, ir.TK_Option) {
				if vr, ok := st.Cond.(*ir.VarRef); !ok {
					pc.Diag.Report(diag.ErrInvalidGuardOptionUnwrap, st.Cond.Span())
				} else {
					tCondType, _ := pc.Types.GetByID(tCond)
					s, _ := pc.Pkg.Syms.GetByID(vr.Ref.Sym)
					oldTyp := s.Typ
					pc.Pkg.Syms.SetType(vr.Ref.Sym, tCondType.ElemType)
					defer pc.Pkg.Syms.SetType(vr.Ref.Sym, oldTyp)

					/*s.Se(tCondType.ElemType) // Unwrap the option type*/
				}
			}

			for _, ret := range st.Returns {
				p.synthesizeTypeFromExpr(pc, ret)
			}
		case *ir.ForStmt:
			if st.CondInt != nil {
				if st.CondInt.Init != nil {
					ti := p.synthesizeTypeFromExpr(pc, st.CondInt.Init.Init)
					expected := pc.Types.PrimInt()
					if ok, _ := isTypeAssignable(pc.Types, expected, ti); !ok {
						exTy, _ := pc.Types.GetByID(expected)
						tiTy, _ := pc.Types.GetByID(ti)
						pc.Diag.Report(diag.ErrTypeMismatch, st.CondInt.Init.Span(), exTy.Key, tiTy.Key)
					}
				}
				if st.CondInt.Cond != nil {
					tCond := p.synthesizeTypeFromExpr(pc, st.CondInt.Cond)
					if tCond != pc.Types.PrimBool() {
						pc.Diag.Report(diag.ErrTypeMismatch, st.CondInt.Cond.Span(), "bool", typeName(pc, tCond))
					}
				}
				if st.CondInt.Post != nil {
					p.synthesizeTypeFromExpr(pc, st.CondInt.Post)
				}
			} else if st.CondIn != nil {
				scope, _ := pc.Pkg.Scopes.EnclosingScope(st.ID())
				ti := p.synthesizeTypeFromExpr(pc, st.CondIn.IterExpr)
				iterTy, _ := pc.Types.GetByID(ti)

				var expectedKeyType ir.TypID
				switch iterTy.Kind {
				case ir.TK_Array, ir.TK_Slice:
					expectedKeyType = pc.Types.PrimInt()
				case ir.TK_Map:
					expectedKeyType = iterTy.KeyType
				default:
					pc.Diag.Report(diag.ErrTypeMismatch, st.CondIn.IterExpr.Span(), "iterable type", iterTy.Key)
				}

				firstVarSym, _ := pc.Pkg.Scopes.Lookup(scope, st.CondIn.InFirstVar.Name)
				if firstVarSym != 0 {
					firstVarSymEntry, _ := pc.Pkg.Syms.GetByID(firstVarSym)
					if ok, _ := isTypeAssignable(pc.Types, firstVarSymEntry.Typ, expectedKeyType); !ok {
						exTy, _ := pc.Types.GetByID(firstVarSymEntry.Typ)
						expTy, _ := pc.Types.GetByID(expectedKeyType)
						pc.Diag.Report(diag.ErrTypeMismatch, st.CondIn.InFirstVar.Span, exTy.Key, expTy.Key)
					}
				}

				if st.CondIn.InSecondVar != nil {
					secondVarSym, _ := pc.Pkg.Scopes.Lookup(scope, st.CondIn.InSecondVar.Name)
					if secondVarSym != 0 {
						secondVarSymEntry, _ := pc.Pkg.Syms.GetByID(secondVarSym)
						if iterTy.Kind == ir.TK_Map {
							if ok, _ := isTypeAssignable(pc.Types, secondVarSymEntry.Typ, iterTy.ValueType); !ok {
								exTy, _ := pc.Types.GetByID(secondVarSymEntry.Typ)
								expTy, _ := pc.Types.GetByID(iterTy.ValueType)
								pc.Diag.Report(diag.ErrTypeMismatch, st.CondIn.InSecondVar.Span, exTy.Key, expTy.Key)
							}
						} else if iterTy.Kind == ir.TK_Array || iterTy.Kind == ir.TK_Slice {
							pc.Pkg.Syms.SetType(secondVarSym, pc.Types.PrimInt())
						} else {
							pc.Diag.Report(diag.ErrTypeMismatch, st.CondIn.IterExpr.Span(), "iterable type", iterTy.Key)
						}
					}
				}

				if st.CondIn.InThirdVar != nil {
					if iterTy.Kind == ir.TK_Map {
						thirdVarSym, _ := pc.Pkg.Scopes.Lookup(scope, st.CondIn.InThirdVar.Name)
						if thirdVarSym != 0 {
							pc.Pkg.Syms.SetType(thirdVarSym, pc.Types.PrimInt())
						}
					} else {
						pc.Diag.Report(diag.ErrTypeMismatch, st.CondIn.InThirdVar.Span, "map type for index variable", iterTy.Key)
					}
				}
			} else if st.CondRange != nil {
				tStart := p.synthesizeTypeFromExpr(pc, st.CondRange.RangeStart)
				tEnd := p.synthesizeTypeFromExpr(pc, st.CondRange.RangeEnd)
				expected := pc.Types.PrimInt()

				if ok, _ := isTypeAssignable(pc.Types, expected, tStart); !ok {
					exTy, _ := pc.Types.GetByID(expected)
					tStartTy, _ := pc.Types.GetByID(tStart)
					pc.Diag.Report(diag.ErrTypeMismatch, st.CondRange.RangeStart.Span(), exTy.Key, tStartTy.Key)
				}

				if ok, _ := isTypeAssignable(pc.Types, expected, tEnd); !ok {
					exTy, _ := pc.Types.GetByID(expected)
					tEndTy, _ := pc.Types.GetByID(tEnd)
					pc.Diag.Report(diag.ErrTypeMismatch, st.CondRange.RangeEnd.Span(), exTy.Key, tEndTy.Key)
				}
			}

			p.resolveStmts(pc, st.Body.Stmts)
		case *ir.WhileStmt:
			tCond := p.synthesizeTypeFromExpr(pc, st.Cond)
			if tCond != pc.Types.PrimBool() {
				pc.Diag.Report(diag.ErrTypeMismatch, st.Cond.Span(), "bool", typeName(pc, tCond))
			}

			p.resolveStmts(pc, st.Body.Stmts)
		}
	}
}

func (p *PassInferTypes) synthesizeTypeFromExpr(pc *PassContext, expr ir.Expr) ir.TypID {
	tt := pc.Types
	sa := pc.Pkg.Syms
	switch x := expr.(type) {
	case *ir.WhenExpr:
		valueType := p.synthesizeTypeFromExpr(pc, x.Expr)

		var returnType ir.TypID
		for _, c := range x.Cases {
			for _, v := range c.Values {
				tVal := p.synthesizeTypeFromExpr(pc, v)
				if valueType == 0 {
					valueType = tVal
				} else if ok, _ := isTypeAssignable(tt, valueType, tVal); !ok {
					pc.Diag.Report(diag.ErrTypeMismatch, v.Span(), typeName(pc, valueType), typeName(pc, tVal))
				}
			}

			tRet := p.synthesizeTypeFromExpr(pc, c.Then)
			if returnType == 0 {
				returnType = tRet
			} else if ok, _ := isTypeAssignable(tt, returnType, tRet); !ok {
				returnType = tt.PrimAny() // If types are not assignable, use 'any' type
			}
		}

		tDef := p.synthesizeTypeFromExpr(pc, x.Default)
		if returnType == 0 {
			returnType = tDef
		} else if ok, _ := isTypeAssignable(tt, returnType, tDef); !ok {
			returnType = tt.PrimAny() // If types are not assignable, use 'any' type
		}

		x.SetType(returnType)

		return returnType
	case *ir.UnaryExpr:
		t := p.synthesizeTypeFromExpr(pc, x.Expr)
		switch x.Op {
		case ir.OpLNot: // !
			if t != tt.PrimBool() {
				pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "bool", typeName(pc, t))
				t = tt.TypError()
			}
			x.SetType(tt.PrimBool())
			return tt.PrimBool()
		case ir.OpAdd, ir.OpSub: // +x, -x
			if t != tt.PrimInt() && t != tt.PrimFloat() {
				pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int or float", typeName(pc, t))
				t = tt.TypError()
			}
			x.SetType(t)
			return t
		case ir.OpNot: // ~ (bitwise not)
			if t != tt.PrimInt() {
				pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int", typeName(pc, t))
				t = tt.TypError()
			}
			x.SetType(t)
			return t
		default:
			x.SetType(tt.TypError())
			return tt.TypError()
		}
	case *ir.PrefixUnaryExpr:
		t := p.synthesizeTypeFromExpr(pc, x.Expr)
		if t != tt.PrimInt() && t != tt.PrimFloat() {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int or float", typeName(pc, t))
			t = tt.TypError()
		}
		x.SetType(t)
		return t

	case *ir.PostfixUnaryExpr:
		t := p.synthesizeTypeFromExpr(pc, x.Expr)
		if t != tt.PrimInt() && t != tt.PrimFloat() {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int or float", typeName(pc, t))
			t = tt.TypError()
		}
		x.SetType(t)
		return t
	case *ir.BinaryExpr:
		l := p.synthesizeTypeFromExpr(pc, x.Left)
		r := p.synthesizeTypeFromExpr(pc, x.Right)

		isNum := func(t ir.TypID) bool { return t == tt.PrimInt() || t == tt.PrimFloat() }
		commonNum := func(a, b ir.TypID) (ir.TypID, bool) {
			if a == b && isNum(a) {
				return a, true
			}
			if isNum(a) && isNum(b) {
				return tt.PrimFloat(), true
			}
			return 0, false
		}

		switch x.Op {
		case ir.OpAdd, ir.OpSub, ir.OpMul, ir.OpDiv:
			if x.Op == ir.OpAdd && l == tt.PrimString() || r == tt.PrimString() { // string concatenation
				x.SetType(tt.PrimString())
				return tt.PrimString()
			}
			if t, ok := commonNum(l, r); ok {
				x.SetType(t)
				return t
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "numeric (int or float)", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		case ir.OpMod:
			if l == tt.PrimInt() && r == tt.PrimInt() {
				x.SetType(tt.PrimInt())
				return tt.PrimInt()
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int % int", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		case ir.OpAnd, ir.OpOr, ir.OpXor:
			if l == tt.PrimInt() && r == tt.PrimInt() {
				x.SetType(tt.PrimInt())
				return tt.PrimInt()
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int (bitwise)", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		case ir.OpShl, ir.OpShr:
			if l == tt.PrimInt() && r == tt.PrimInt() {
				x.SetType(tt.PrimInt())
				return tt.PrimInt()
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "int << int / int >> int", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		case ir.OpLAnd, ir.OpLOr:
			if l == tt.PrimBool() && r == tt.PrimBool() {
				x.SetType(tt.PrimBool())
				return tt.PrimBool()
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "bool && bool / bool || bool", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		case ir.OpEq, ir.OpNeq:
			if _, ok := commonNum(l, r); ok {
				x.SetType(tt.PrimBool())
				return tt.PrimBool()
			}
			if okAB, _ := isTypeAssignable(tt, l, r); okAB {
				x.SetType(tt.PrimBool())
				return tt.PrimBool()
			}
			if okBA, _ := isTypeAssignable(tt, r, l); okBA {
				x.SetType(tt.PrimBool())
				return tt.PrimBool()
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "comparable types for ==", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		case ir.OpLt, ir.OpLte, ir.OpGt, ir.OpGte:
			if _, ok := commonNum(l, r); ok {
				x.SetType(tt.PrimBool())
				return tt.PrimBool()
			}
			pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), "numeric comparison", typeName(pc, l)+", "+typeName(pc, r))
			x.SetType(tt.TypError())
			return tt.TypError()

		default:
			pc.Diag.Report(diag.ErrInvalidOperator, x.Span(), "unknown binary op: "+string(x.Op))
			x.SetType(tt.TypError())
			return tt.TypError()
		}
	case *ir.CoalesceExpr:
		tLeft := p.synthesizeTypeFromExpr(pc, x.Left)
		tDefault := p.synthesizeTypeFromExpr(pc, x.Default)

		leftTy, ok := tt.GetByID(tLeft)
		if !ok || (leftTy.Kind != ir.TK_Option && leftTy.Kind != ir.TK_PrimitiveNone) {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Left.Span(), "option<T>", typeName(pc, tLeft))
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		if ok, _ := isTypeAssignable(tt, leftTy.ElemType, tDefault); !ok {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Default.Span(), typeName(pc, leftTy.ElemType), typeName(pc, tDefault))
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		x.SetType(leftTy.ElemType)
		return leftTy.ElemType
	case *ir.TenaryExpr:
		tc := p.synthesizeTypeFromExpr(pc, x.Cond)
		if tc != tt.PrimBool() {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Cond.Span(), "bool", typeName(pc, tc))
		}

		tThen := p.synthesizeTypeFromExpr(pc, x.Then)
		tElse := p.synthesizeTypeFromExpr(pc, x.Else)

		if tThen == tElse && tThen != 0 {
			x.SetType(tThen)
			return tThen
		}

		if t, ok := func() (ir.TypID, bool) {
			isNum := func(t ir.TypID) bool { return t == tt.PrimInt() || t == tt.PrimFloat() }
			if isNum(tThen) && isNum(tElse) {
				if tThen == tElse {
					return tThen, true
				}
				return tt.PrimFloat(), true
			}
			return 0, false
		}(); ok {
			x.SetType(t)
			return t
		}
		if ok, _ := isTypeAssignable(tt, tThen, tElse); ok {
			x.SetType(tElse)
			return tElse
		}
		if ok, _ := isTypeAssignable(tt, tElse, tThen); ok {
			x.SetType(tThen)
			return tThen
		}

		pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), typeName(pc, tThen), typeName(pc, tElse))
		x.SetType(tt.TypError())
		return tt.TypError()
	case *ir.GroupedExpr:
		t := p.synthesizeTypeFromExpr(pc, x.Expr)
		x.SetType(t)
		return t
	case *ir.AssignmentExpr:
		leftSym, ok := sa.GetByID(x.Left.Sym)
		if !ok {
			return tt.TypError()
		}
		tRight := p.synthesizeTypeFromExpr(pc, x.Right)
		if ok, _ := isTypeAssignable(tt, leftSym.Typ, tRight); ok {
			x.SetType(leftSym.Typ)
			return leftSym.Typ
		}

		leftTy, _ := tt.GetByID(leftSym.Typ)
		tRightTy, _ := tt.GetByID(tRight)
		pc.Diag.Report(diag.ErrTypeMismatch, x.Span(), leftTy.Key, tRightTy.Key)

		x.SetType(tt.TypError())
		return tt.TypError()
	case *ir.IndexExpr:
		tBase := p.synthesizeTypeFromExpr(pc, x.Expr)
		baseTy, ok := tt.GetByID(tBase)
		tIndex := p.synthesizeTypeFromExpr(pc, x.Index)
		tIndexTy, _ := tt.GetByID(tIndex)
		if !ok {
			return tt.TypError()
		}

		switch baseTy.Kind {
		case ir.TK_Array, ir.TK_Slice:
			if tIndex != tt.PrimInt() {
				pc.Diag.Report(diag.ErrTypeMismatch, x.Index.Span(), "int", tIndexTy)
				return tt.TypError()
			}
			x.SetType(baseTy.ElemType)
			return baseTy.ElemType
		case ir.TK_Map:
			if ok, _ := isTypeAssignable(tt, baseTy.KeyType, tIndex); !ok {
				pc.Diag.Report(diag.ErrTypeMismatch, x.Index.Span(), baseTy.KeyType, tIndexTy)
				return tt.TypError()
			}
			x.SetType(baseTy.ValueType)
			return baseTy.ValueType
		default:
			pc.Diag.Report(diag.ErrTypeNotIndexable, x.Expr.Span(), baseTy.Key)
			return tt.TypError()
		}
	case *ir.FieldAccessExpr:
		cur := p.synthesizeTypeFromExpr(pc, x.Expr)
		for _, fld := range x.Fields {
			ty, ok := tt.GetByID(cur)
			if !ok {
				pc.Diag.Report(diag.ErrUnknownType, fld.Span, "base")
				x.SetType(tt.TypError())
				return tt.TypError()
			}
			switch ty.Kind {
			case ir.TK_Map:
				if ty.KeyType != tt.PrimString() && ty.KeyType != tt.PrimAny() {
					ktName := typeName(pc, ty.KeyType)
					pc.Diag.Report(diag.ErrTypeMismatch, fld.Span, "map<string, _>", "map<"+ktName+", _>")
					x.SetType(tt.TypError())
					return tt.TypError()
				}
				cur = ty.ValueType

			case ir.TK_Enum:
				// Check if field is a case name (e.g., Color.Red)
				foundCase := false
				for _, c := range ty.EnumCases {
					if c.Name == fld.Name {
						foundCase = true
						// The type of an enum case is the enum itself
						cur = cur // Stay the same enum type
						break
					}
				}

				// If not a case, check if it's a field name (e.g., state.message)
				if !foundCase {
					foundField := false
					for _, f := range ty.EnumFields {
						if f.Name == fld.Name {
							foundField = true
							cur = f.Type
							break
						}
					}

					// If not a field, check if it's a method name (e.g., state.display)
					if !foundField {
						foundMethod := false
						for _, m := range ty.EnumMethods {
							if m.Name == fld.Name {
								foundMethod = true
								cur = m.Type
								break
							}
						}

						if !foundMethod {
							pc.Diag.Report(diag.ErrTypeNotIndexable, fld.Span, fmt.Sprintf("enum %s has no case, field, or method named '%s'", ty.EnumName, fld.Name))
							x.SetType(tt.TypError())
							return tt.TypError()
						}
					}
				}

			default:
				baseName := typeName(pc, cur)
				pc.Diag.Report(diag.ErrTypeNotIndexable, fld.Span, baseName)
				x.SetType(tt.TypError())
				return tt.TypError()
			}
		}
		x.SetType(cur)
		return cur
	case *ir.VarRef:
		s, ok := sa.GetByID(x.Ref.Sym)
		if !ok {
			pc.Diag.Report(diag.ErrUndeclaredSymbol, x.Span(), x.Ref.Name)
			return tt.TypError()
		}
		x.SetType(s.Typ)
		return s.Typ
	case *ir.RangeExpr:
		tStart := p.synthesizeTypeFromExpr(pc, x.Start)
		tEnd := p.synthesizeTypeFromExpr(pc, x.End)
		if tStart != tt.PrimInt() && tStart != tt.PrimFloat() {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Start.Span(), "int or float", typeName(pc, tStart))
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		if tEnd != tt.PrimInt() && tEnd != tt.PrimFloat() {
			pc.Diag.Report(diag.ErrTypeMismatch, x.End.Span(), "int or float", typeName(pc, tEnd))
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		if ok, _ := isTypeAssignable(tt, tEnd, tStart); !ok {
			pc.Diag.Report(diag.ErrTypeMismatch, x.End.Span(), typeName(pc, tStart), typeName(pc, tEnd))
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		if x.Inc != nil {
			tInc := p.synthesizeTypeFromExpr(pc, x.Inc)
			if ok, _ := isTypeAssignable(tt, tStart, tInc); !ok {
				pc.Diag.Report(diag.ErrTypeMismatch, x.Inc.Span(), typeName(pc, tStart), typeName(pc, tInc))
				x.SetType(tt.TypError())
				return tt.TypError()
			}
		}

		retType := tt.SliceOf(tStart)
		x.SetType(retType)
		return retType
	case *ir.FuncCallExpr:
		prelimArgTypes := make([]ir.TypID, len(x.Args))
		for i, arg := range x.Args {
			prelimArgTypes[i] = p.synthesizeTypeFromExpr(pc, arg.Expr)
		}

		if varRef, ok := x.Callee.(*ir.VarRef); ok {
			scope, _ := pc.Pkg.Scopes.EnclosingScope(x.ID())
			candidates := pc.Pkg.Scopes.LookupAll(scope, varRef.Ref.Name)

			if len(candidates) > 1 {
				bestMatch := p.resolveOverload(pc, candidates, prelimArgTypes)
				if bestMatch != 0 {
					varRef.Ref.Sym = bestMatch
				}
			}
		}

		funcTy := p.synthesizeTypeFromExpr(pc, x.Callee)
		funcTyDef, ok := tt.GetByID(funcTy)
		if !ok || funcTyDef.Kind != ir.TK_Function {
			pc.Diag.Report(diag.ErrTypeMismatch, x.Callee.Span(), "function", typeName(pc, funcTy))
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		hasNamedArgs := false
		for _, arg := range x.Args {
			if arg.Name != "" {
				hasNamedArgs = true
				break
			}
		}

		var argTypes []ir.TypID
		if hasNamedArgs {
			argTypes = make([]ir.TypID, len(funcTyDef.ParamTypes))
			reorderedArgs := make([]ir.FuncCallArg, len(funcTyDef.ParamTypes))
			positionalIndex := 0
			used := make([]bool, len(x.Args))

			for i, arg := range x.Args {
				if arg.Name == "" {
					if positionalIndex >= len(funcTyDef.ParamTypes) {
						pc.Diag.Report(diag.ErrFuncParamMismatch, x.Span(), funcTyDef.Key, "too many positional arguments")
						x.SetType(tt.TypError())
						return tt.TypError()
					}
					reorderedArgs[positionalIndex] = arg
					used[i] = true
					positionalIndex++
				}
			}

			for i, arg := range x.Args {
				if used[i] {
					continue
				}

				paramIndex := -1
				for pi, param := range funcTyDef.ParamTypes {
					if param.Name.Name == arg.Name {
						paramIndex = pi
						break
					}
				}
				if paramIndex == -1 {
					pc.Diag.Report(diag.ErrFuncParamMismatch, x.Span(), funcTyDef.Key, fmt.Sprintf("unknown parameter name '%s'", arg.Name))
					x.SetType(tt.TypError())
					return tt.TypError()
				}
				if reorderedArgs[paramIndex].Expr != nil {
					pc.Diag.Report(diag.ErrFuncParamMismatch, x.Span(), funcTyDef.Key, fmt.Sprintf("parameter '%s' specified multiple times", arg.Name))
					x.SetType(tt.TypError())
					return tt.TypError()
				}

				reorderedArgs[paramIndex] = arg
			}

			x.Args = reorderedArgs

			for i, arg := range reorderedArgs {
				if arg.Expr != nil {
					argTypes[i] = p.synthesizeTypeFromExpr(pc, arg.Expr)
				} else {
					argTypes[i] = 0
				}
			}
		} else {
			argTypes = make([]ir.TypID, len(x.Args))
			for i, arg := range x.Args {
				argTypes[i] = p.synthesizeTypeFromExpr(pc, arg.Expr)
			}
		}

		if ok, reason := assertFunctionParameterCompatibility(pc, tt, funcTyDef, argTypes); !ok {
			pc.Diag.Report(diag.ErrFuncParamMismatch, x.Span(), funcTyDef.Key, reason)
			x.SetType(tt.TypError())
			return tt.TypError()
		}

		x.SetType(funcTyDef.ReturnType)
		return funcTyDef.ReturnType
	case *ir.FuncLitExpr:
		if x.GetType() != 0 {
			return x.GetType()
		}

		for _, param := range x.Params {
			if param.Type != nil && param.Type.Typ != 0 {
				pc.Pkg.Syms.SetType(param.Name.Sym, param.Type.Typ)
			} else if param.Default != nil {
				ti := p.synthesizeTypeFromExpr(pc, param.Default)
				pc.Pkg.Syms.SetType(param.Name.Sym, ti)
			} else {
				pc.Pkg.Syms.SetType(param.Name.Sym, pc.Types.TypError())
				pc.Diag.Report(diag.ErrTypeInferenceFailed, param.Name.Span, fmt.Sprintf("parameter '%s'", param.Name.Name))
			}
		}

		funcTyp := pc.Types.FuncOf(x.Params, x.ReturnType.Typ)
		x.SetType(funcTyp)

		p.resolveStmts(pc, x.Body.Stmts)

		return funcTyp
	case *ir.LitInt:
		t := tt.PrimInt()
		x.SetType(t)
		return t
	case *ir.LitFloat:
		t := tt.PrimFloat()
		x.SetType(t)
		return t
	case *ir.LitBool:
		t := tt.PrimBool()
		x.SetType(t)
		return t
	case *ir.LitString:
		t := tt.PrimString()
		x.SetType(t)
		return t
	case *ir.LitChar:
		t := tt.PrimChar()
		x.SetType(t)
		return t
	case *ir.LitNone:
		t := tt.TypNone()
		x.SetType(t)
		return t
	case *ir.ArrayLiteral:
		if len(x.Elems) == 0 {
			return tt.TypError()
		}
		et := p.synthesizeTypeFromExpr(pc, x.Elems[0])
		for i := 1; i < len(x.Elems); i++ {
			et2 := p.synthesizeTypeFromExpr(pc, x.Elems[i])
			if ok, _ := isTypeAssignable(tt, et, et2); !ok {
				et = tt.PrimAny() // If types are not assignable, use 'any' type
				break
			} else {
				et = et2 // Keep the type consistent
			}
		}

		t := tt.ArrayOf(et, int64(len(x.Elems)))
		x.SetType(t)
		return t
	case *ir.MapLiteral:
		if len(x.Entries) == 0 {
			return tt.TypError()
		}
		kt := p.synthesizeTypeFromExpr(pc, x.Entries[0].Key)
		vt := p.synthesizeTypeFromExpr(pc, x.Entries[0].Value)
		for _, entry := range x.Entries[1:] {
			kt2 := p.synthesizeTypeFromExpr(pc, entry.Key)
			vt2 := p.synthesizeTypeFromExpr(pc, entry.Value)
			if ok, _ := isTypeAssignable(tt, kt, kt2); !ok {
				kt = tt.PrimAny() // If key types are not assignable, use 'any' type
			}

			if ok, _ := isTypeAssignable(tt, vt, vt2); !ok {
				vt = tt.PrimAny() // If value types are not assignable, use 'any' type
			}
		}
		t := tt.MapOf(kt, vt)
		x.SetType(t)
		return t
	case *ir.TupleLiteral:
		if len(x.Elems) == 0 {
			return tt.TypError()
		}
		fields := make([]ir.TupleField, len(x.Elems))
		for i, el := range x.Elems {
			et := p.synthesizeTypeFromExpr(pc, el)
			fields[i] = ir.TupleField{Type: et}
		}
		t := tt.TupleOf(fields...)
		x.SetType(t)
		return t
	default:
		return tt.TypError()
	}
}

// isTypeAssignable checks if the source type can be assigned to the destination type.
func isTypeAssignable(tt *ir.TypeTable, dst, src ir.TypID) (bool, string) {
	if dst == src {
		return true, "same type"
	}
	if dst == 0 || src == 0 {
		return false, "unknown type"
	}
	if dst == tt.PrimAny() || src == tt.PrimAny() {
		return true, "any type is assignable to any other type"
	}
	if (dst == tt.PrimFloat() && src == tt.PrimInt()) || (dst == tt.PrimInt() && src == tt.PrimFloat()) {
		return true, "implicit conversion between int and float"
	}
	if dstTy, _ := tt.GetByID(dst); dstTy != nil && dstTy.Kind == ir.TK_Option {
		if src == tt.TypNone() {
			return true, "none to option"
		}

		if srcTy, _ := tt.GetByID(src); srcTy != nil && srcTy.Kind == ir.TK_Option {
			if dstTy.ElemType == srcTy.ElemType {
				return true, "option to option with same element type"
			}
			return false, "option element type mismatch"
		}

		if src == dstTy.ElemType {
			return true, "implicit lifting T to option<T>"
		}

		if ok, _ := isTypeAssignable(tt, dstTy.ElemType, src); ok {
			return true, "implicit lifting with conversion"
		}

		return false, "incompatible type for option"
	}

	if tsStructEqual(tt, dst, src) {
		return true, ""
	}

	return false, "types are not assignable"
}

// assertFunctionParameterCompatibility checks if the provided argument types are compatible with the function's parameter types.
// It also checks if the counts of parameters and arguments match, considering variadic parameters and default values.
func assertFunctionParameterCompatibility(pc *PassContext, tt *ir.TypeTable, funcType *ir.Type, argTypes []ir.TypID) (bool, string) {
	paramCount := len(funcType.ParamTypes)
	argCount := len(argTypes)

	if paramCount == 0 && argCount == 0 {
		return true, "no parameters and no arguments"
	}

	requiredParamCount := 0
	for _, param := range funcType.ParamTypes {
		if param.Default == nil && !param.IsVariadic {
			requiredParamCount++
		}
	}

	isVariadic := false
	if paramCount > 0 {
		lastParam := funcType.ParamTypes[paramCount-1]
		isVariadic = lastParam.IsVariadic
	}

	if !isVariadic {
		if argCount < requiredParamCount {
			return false, fmt.Sprintf("not enough arguments: expected at least %d, got %d", requiredParamCount, argCount)
		}
		if argCount > paramCount {
			return false, fmt.Sprintf("too many arguments: expected at most %d, got %d", paramCount, argCount)
		}
	} else {
		nonVariadicRequired := requiredParamCount
		if funcType.ParamTypes[paramCount-1].IsVariadic {
			nonVariadicRequired = paramCount - 1
			for i := 0; i < paramCount-1; i++ {
				if funcType.ParamTypes[i].Default != nil {
					nonVariadicRequired--
				}
			}
		}
		if argCount < nonVariadicRequired {
			return false, fmt.Sprintf("not enough arguments for variadic function: expected at least %d, got %d", nonVariadicRequired, argCount)
		}
	}

	for i, param := range funcType.ParamTypes {
		if isVariadic && i == paramCount-1 {
			variadicType := param.Type
			for j := i; j < argCount; j++ {
				argType := argTypes[j]
				if ok, _ := isTypeAssignable(tt, variadicType.Typ, argType); !ok {
					return false, "variadic argument type mismatch"
				}
			}
			break
		} else {
			if i >= argCount {
				if param.Default == nil {
					return false, fmt.Sprintf("missing required argument at position %d", i)
				}
				continue
			}

			argType := argTypes[i]
			if argType != 0 {
				if ok, _ := isTypeAssignable(tt, param.Type.Typ, argType); !ok {
					return false, fmt.Sprintf("argument type mismatch wanted %s, got %s", typeName(pc, param.Type.Typ), typeName(pc, argType))
				}
			} else {
				if param.Default == nil {
					return false, fmt.Sprintf("missing required argument at position %d", i)
				}
			}
		}
	}

	return true, "all parameters and arguments are compatible"
}

// tsStructEqual checks if two types are structurally equal, considering their kind and properties.
// This is used for checking if two types are the same in a structural way, such as slices, arrays, maps, tuples, etc.
func tsStructEqual(tt *ir.TypeTable, a, b ir.TypID) bool {
	if a == b {
		return true
	}
	ta, okA := tt.GetByID(a)
	tb, okB := tt.GetByID(b)
	if !okA || !okB {
		return false
	}
	if ta.Kind != tb.Kind {
		return false
	}
	switch ta.Kind {
	case ir.TK_Array:
		return ta.ElemType == tb.ElemType && ta.Dim == tb.Dim && ta.Key == "" && tb.Key == ""
	case ir.TK_Slice:
		return ta.ElemType == tb.ElemType && ta.Key == "" && tb.Key == ""
	case ir.TK_Map:
		return ta.KeyType == tb.KeyType && ta.ValueType == tb.ValueType
	case ir.TK_Tuple:
		if len(ta.Fields) != len(tb.Fields) {
			return false
		}
		for i := range ta.Fields {
			if ta.Fields[i].Type != tb.Fields[i].Type { // Positional sensitivity is important here
				return false
			}
		}
		return true
	case ir.TK_Function:
		if len(ta.ParamTypes) != len(tb.ParamTypes) {
			return false
		}

		for i := range ta.ParamTypes {
			pa := ta.ParamTypes[i]
			pb := tb.ParamTypes[i]
			if pa.Type != pb.Type || pa.IsVariadic != pb.IsVariadic {
				return false
			}
		}

		if ta.ReturnType != tb.ReturnType {
			return false
		}

		return true
	default:
		return false
	}
}

func typeName(pc *PassContext, t ir.TypID) string {
	ty, ok := pc.Types.GetByID(t)
	if !ok {
		return "unknown"
	}
	return string(ty.Key)
}

// resolveOverload selects the best matching function overload based on argument types.
// Returns the symbol ID of the best match, or 0 if no match found.
func (p *PassInferTypes) resolveOverload(pc *PassContext, candidates []ir.SymID, argTypes []ir.TypID) ir.SymID {
	tt := pc.Types
	sa := pc.Pkg.Syms

	var bestMatch ir.SymID
	var bestScore = -1

	for _, candSym := range candidates {
		symbol, ok := sa.GetByID(candSym)
		if !ok || symbol.Kind != ir.SK_Function {
			continue
		}

		funcType, ok := tt.GetByID(symbol.Typ)
		if !ok || funcType.Kind != ir.TK_Function {
			continue
		}

		paramCount := len(funcType.ParamTypes)
		argCount := len(argTypes)

		requiredParams := 0
		for _, param := range funcType.ParamTypes {
			if param.Default == nil && !param.IsVariadic {
				requiredParams++
			}
		}

		if argCount < requiredParams || argCount > paramCount {
			continue // Incompatible argument count
		}

		score := 0
		compatible := true

		for i := 0; i < argCount && i < paramCount; i++ {
			paramType := funcType.ParamTypes[i].Type.Typ
			argType := argTypes[i]

			if paramType == argType {
				score += 10 // Exact match
			} else if ok, _ := isTypeAssignable(tt, paramType, argType); ok {
				score += 5 // Compatible but not exact
			} else {
				compatible = false
				break
			}
		}

		if compatible && score > bestScore {
			bestScore = score
			bestMatch = candSym
		}
	}

	return bestMatch
}

// isTerminator checks if a statement is a terminator (return, break, continue).
// Terminators make subsequent statements in the same block unreachable.
func (p *PassInferTypes) isTerminator(st ir.Stmt) bool {
	switch st.(type) {
	case *ir.ReturnStmt, *ir.BreakStmt, *ir.ContinueStmt:
		return true
	default:
		return false
	}
}

// collectReturnTypes collects all return statement types from a list of statements.
func (p *PassInferTypes) collectReturnTypes(pc *PassContext, stmts []ir.Stmt) []struct {
	Typ  ir.TypID
	Span diag.TextSpan
} {
	var types []struct {
		Typ  ir.TypID
		Span diag.TextSpan
	}

	for _, st := range stmts {
		switch s := st.(type) {
		case *ir.ReturnStmt:
			if len(s.Results) == 0 {
				types = append(types, struct {
					Typ  ir.TypID
					Span diag.TextSpan
				}{Typ: pc.Types.TypNone(), Span: s.Span()})
			} else if len(s.Results) == 1 {
				typ := p.synthesizeTypeFromExpr(pc, s.Results[0])
				types = append(types, struct {
					Typ  ir.TypID
					Span diag.TextSpan
				}{Typ: typ, Span: s.Span()})
			} else {
				var fields []ir.TupleField
				for _, result := range s.Results {
					typ := p.synthesizeTypeFromExpr(pc, result)
					fields = append(fields, ir.TupleField{Name: "", Type: typ})
				}
				tupleTyp := pc.Types.TupleOf(fields...)
				types = append(types, struct {
					Typ  ir.TypID
					Span diag.TextSpan
				}{Typ: tupleTyp, Span: s.Span()})
			}
		case *ir.BlockStmt:
			types = append(types, p.collectReturnTypes(pc, s.Stmts)...)
		case *ir.IfStmt:
			types = append(types, p.collectReturnTypes(pc, s.Then.Stmts)...)
			for _, elif := range s.ElseIfs {
				types = append(types, p.collectReturnTypes(pc, elif.Then.Stmts)...)
			}
			if s.Else != nil {
				types = append(types, p.collectReturnTypes(pc, s.Else.Stmts)...)
			}
		case *ir.SwitchStmt:
			for _, c := range s.Cases {
				types = append(types, p.collectReturnTypes(pc, c.Stmts)...)
			}
			if s.Default != nil {
				types = append(types, p.collectReturnTypes(pc, s.Default)...)
			}
		case *ir.ForStmt:
			types = append(types, p.collectReturnTypes(pc, s.Body.Stmts)...)
		case *ir.WhileStmt:
			types = append(types, p.collectReturnTypes(pc, s.Body.Stmts)...)
		}
	}

	return types
}

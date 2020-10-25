// tslint:disable:ban-types

// Don't change, this corresponds to DateTime.Kind enum values in .NET
export const enum DateKind {
  Unspecified = 0,
  UTC = 1,
  Local = 2,
}

export interface IDateTime extends Date {
  kind?: DateKind;
}

export interface IDateTimeOffset extends Date {
  offset?: number;
}

export interface IComparable<T> extends IEquatable<T> {
  CompareTo(x: T): number;
}

export interface IEquatable<T> {
  GetHashCode(): number;
  Equals(x: T): boolean;
}

export interface IDisposable {
  Dispose(): void;
}

export interface IComparer<T> {
  Compare(x: T, y: T): number;
}

export interface IEqualityComparer<T> {
  Equals(x: T, y: T): boolean;
  GetHashCode(x: T): number;
}

export interface ICollection<T> {
  readonly Count: number;
  readonly IsReadOnly: boolean;
  Add(item: T): void;
  Clear(): void;
  Contains(item: T): boolean;
  CopyTo(array: T[], arrayIndex: number): void;
  Remove(item: T): boolean;
}

export function isIterable<T>(x: T | Iterable<T>): x is Iterable<T> {
  return x != null && typeof x === "object" && Symbol.iterator in x;
}

export function isArrayLike<T>(x: T | ArrayLike<T>): x is ArrayLike<T> {
  return Array.isArray(x) || ArrayBuffer.isView(x);
}

function isComparer<T>(x: T | IComparer<T>): x is IComparer<T> {
  return typeof (x as IComparer<T>).Compare === "function";
}

function isComparable<T>(x: T | IComparable<T>): x is IComparable<T> {
  return typeof (x as IComparable<T>).CompareTo === "function";
}

function isEquatable<T>(x: T | IEquatable<T>): x is IEquatable<T> {
  return typeof (x as IEquatable<T>).Equals === "function";
}

function isHashable<T>(x: T | IEquatable<T>): x is IEquatable<T> {
  return typeof (x as IEquatable<T>).GetHashCode === "function";
}

export function isDisposable<T>(x: T | IDisposable): x is IDisposable {
  return x != null && typeof (x as IDisposable).Dispose === "function";
}

export function sameConstructor<T>(x: T, y: T) {
  return Object.getPrototypeOf(x).constructor === Object.getPrototypeOf(y).constructor;
}

export class Comparer<T> implements IComparer<T> {
  public Compare: (x: T, y: T) => number;

  constructor(f?: (x: T, y: T) => number) {
    this.Compare = f || compare;
  }
}

export function comparerFromEqualityComparer<T>(comparer: IEqualityComparer<T>): IComparer<T> {
  // Sometimes IEqualityComparer also implements IComparer
  if (isComparer(comparer)) {
    return new Comparer<T>((comparer as any as IComparer<T>).Compare);
  } else {
    return new Comparer<T>((x: T, y: T) => {
      const xhash = comparer.GetHashCode(x);
      const yhash = comparer.GetHashCode(y);
      if (xhash === yhash) {
        return comparer.Equals(x, y) ? 0 : -1;
      } else {
        return xhash < yhash ? -1 : 1;
      }
    });
  }
}

export function assertEqual<T>(actual: T, expected: T, msg?: string): void {
  if (!equals(actual, expected)) {
    throw Object.assign(new Error(msg || `Expected: ${expected} - Actual: ${actual}`), {
      actual,
      expected,
    });
  }
}

export function assertNotEqual<T>(actual: T, expected: T, msg?: string): void {
  if (equals(actual, expected)) {
    throw Object.assign(new Error(msg || `Expected: ${expected} - Actual: ${actual}`), {
      actual,
      expected,
    });
  }
}

export class Lazy<T> {
  public factory: () => T;
  public isValueCreated: boolean;

  private createdValue?: T;

  constructor(factory: () => T) {
    this.factory = factory;
    this.isValueCreated = false;
  }

  get Value() {
    if (!this.isValueCreated) {
      this.createdValue = this.factory();
      this.isValueCreated = true;
    }
    return this.createdValue;
  }

  get IsValueCreated() {
    return this.isValueCreated;
  }
}

export function lazyFromValue<T>(v: T) {
  return new Lazy(() => v);
}

export function padWithZeros(i: number, length: number) {
  let str = i.toString(10);
  while (str.length < length) {
    str = "0" + str;
  }
  return str;
}

export function padLeftAndRightWithZeros(i: number, lengthLeft: number, lengthRight: number) {
  let str = i.toString(10);
  while (str.length < lengthLeft) {
    str = "0" + str;
  }
  while (str.length < lengthRight) {
    str = str + "0";
  }
  return str;
}

export function dateOffset(date: IDateTime | IDateTimeOffset): number {
  const date1 = date as IDateTimeOffset;
  return typeof date1.offset === "number"
    ? date1.offset
    : ((date as IDateTime).kind === DateKind.UTC
      ? 0 : date.getTimezoneOffset() * -60000);
}

export function int16ToString(i: number, radix?: number) {
  i = i < 0 && radix != null && radix !== 10 ? 0xFFFF + i + 1 : i;
  return i.toString(radix);
}

export function int32ToString(i: number, radix?: number) {
  i = i < 0 && radix != null && radix !== 10 ? 0xFFFFFFFF + i + 1 : i;
  return i.toString(radix);
}

export abstract class ObjectRef {
  public static id(o: any) {
    if (!ObjectRef.idMap.has(o)) {
      ObjectRef.idMap.set(o, ++ObjectRef.count);
    }
    return ObjectRef.idMap.get(o);
  }
  private static idMap = new WeakMap();
  private static count = 0;
}

export function stringHash(s: string) {
  let i = 0;
  let h = 5381;
  const len = s.length;
  while (i < len) {
    h = (h * 33) ^ s.charCodeAt(i++);
  }
  return h;
}

export function numberHash(x: number) {
  return x * 2654435761 | 0;
}

// From https://stackoverflow.com/a/37449594
export function combineHashCodes(hashes: number[]) {
  if (hashes.length === 0) { return 0; }
  return hashes.reduce((h1, h2) => {
    return ((h1 << 5) + h1) ^ h2;
  });
}

export function physicalHash<T>(x: T): number {
  if (x == null) {
    return 0;
  }
  switch (typeof x) {
    case "boolean":
      return x ? 1 : 0;
    case "number":
      return numberHash(x);
    case "string":
      return stringHash(x);
    default:
      return numberHash(ObjectRef.id(x));
  }
}

export function identityHash<T>(x: T): number {
  if (x == null) {
    return 0;
  } else if (isHashable(x)) {
    return x.GetHashCode();
  } else {
    return physicalHash(x);
  }
}

export function structuralHash<T>(x: T): number {
  if (x == null) {
    return 0;
  }
  switch (typeof x) {
    case "boolean":
      return x ? 1 : 0;
    case "number":
      return numberHash(x);
    case "string":
      return stringHash(x);
    default: {
      if (isHashable(x)) {
        return x.GetHashCode();
      } else if (isArrayLike(x)) {
        const len = x.length;
        const hashes: number[] = new Array(len);
        for (let i = 0; i < len; i++) {
          hashes[i] = structuralHash(x[i]);
        }
        return combineHashCodes(hashes);
      } else if (x instanceof Date) {
        return x.getTime();
      } else if (Object.getPrototypeOf(x).constructor === Object) {
        // TODO: check call-stack to prevernt cyclic objects?
        const hashes = Object.values(self).map((v) => structuralHash(v));
        return combineHashCodes(hashes);
      } else {
        return stringHash(String(x));
      }
    }
  }
}

export function hashSafe<T>(x: IEquatable<T> | undefined): number {
  return x?.GetHashCode() ?? 0;
}

export function equalArraysWith<T>(x: ArrayLike<T>, y: ArrayLike<T>, eq: (x: T, y: T) => boolean): boolean {
  if (x == null) { return y == null; }
  if (y == null) { return false; }
  if (x.length !== y.length) { return false; }
  for (let i = 0; i < x.length; i++) {
    if (!eq(x[i], y[i])) { return false; }
  }
  return true;
}

export function equalArrays<T>(x: ArrayLike<T>, y: ArrayLike<T>): boolean {
  return equalArraysWith(x, y, equals);
}

function equalObjects(x: { [k: string]: any }, y: { [k: string]: any }): boolean {
  const xKeys = Object.keys(x);
  const yKeys = Object.keys(y);
  if (xKeys.length !== yKeys.length) {
    return false;
  }
  xKeys.sort();
  yKeys.sort();
  for (let i = 0; i < xKeys.length; i++) {
    if (xKeys[i] !== yKeys[i] || !equals(x[xKeys[i]], y[yKeys[i]])) {
      return false;
    }
  }
  return true;
}

export function equalsSafe<T>(x: IEquatable<T> | undefined, y: T): boolean {
  return x?.Equals(y) ?? y == null;
}

export function equals<T>(x: T, y: T): boolean {
  if (x === y) {
    return true;
  } else if (x == null) {
    return y == null;
  } else if (y == null) {
    return false;
  } else if (typeof x !== "object") {
    return false;
  } else if (isEquatable(x)) {
    return x.Equals(y);
  } else if (isArrayLike(x)) {
    return isArrayLike(y) && equalArrays(x, y);
  } else if (x instanceof Date) {
    return (y instanceof Date) && compareDates(x, y) === 0;
  } else {
    return Object.getPrototypeOf(x).constructor === Object && equalObjects(x, y);
  }
}

export function compareDates(x: Date | IDateTime | IDateTimeOffset, y: Date | IDateTime | IDateTimeOffset) {
  let xtime;
  let ytime;

  // DateTimeOffset and DateTime deals with equality differently.
  if ("offset" in x && "offset" in y) {
    xtime = x.getTime();
    ytime = y.getTime();
  } else {
    xtime = x.getTime() + dateOffset(x);
    ytime = y.getTime() + dateOffset(y);
  }

  return xtime === ytime ? 0 : (xtime < ytime ? -1 : 1);
}

export function comparePrimitives(x: any, y: any): number {
  return x === y ? 0 : (x < y ? -1 : 1);
}

export function compareArraysWith<T>(x: ArrayLike<T>, y: ArrayLike<T>, comp: (x: T, y: T) => number): number {
  if (x == null) { return y == null ? 0 : 1; }
  if (y == null) { return -1; }
  if (x.length !== y.length) {
    return x.length < y.length ? -1 : 1;
  }
  for (let i = 0, j = 0; i < x.length; i++) {
    j = comp(x[i], y[i]);
    if (j !== 0) { return j; }
  }
  return 0;
}

export function compareArrays<T>(x: ArrayLike<T>, y: ArrayLike<T>): number {
  return compareArraysWith(x, y, compare);
}

function compareObjects(x: { [k: string]: any }, y: { [k: string]: any }): number {
  const xKeys = Object.keys(x);
  const yKeys = Object.keys(y);
  if (xKeys.length !== yKeys.length) {
    return xKeys.length < yKeys.length ? -1 : 1;
  }
  xKeys.sort();
  yKeys.sort();
  for (let i = 0, j = 0; i < xKeys.length; i++) {
    const key = xKeys[i];
    if (key !== yKeys[i]) {
      return key < yKeys[i] ? -1 : 1;
    } else {
      j = compare(x[key], y[key]);
      if (j !== 0) { return j; }
    }
  }
  return 0;
}

export function compareSafe<T>(x: IComparable<T> | undefined, y: T): number {
  return x?.CompareTo(y) ?? (y == null ? 0 : -1);
}

export function compare<T>(x: T, y: T): number {
  if (x === y) {
    return 0;
  } else if (x == null) {
    return y == null ? 0 : -1;
  } else if (y == null) {
    return 1;
  } else if (typeof x !== "object") {
    return x < y ? -1 : 1;
  } else if (isComparable(x)) {
    return x.CompareTo(y);
  } else if (isArrayLike(x)) {
    return isArrayLike(y) ? compareArrays(x, y) : -1;
  } else if (x instanceof Date) {
    return y instanceof Date ? compareDates(x, y) : -1;
  } else {
    return Object.getPrototypeOf(x).constructor === Object ? compareObjects(x, y) : -1;
  }
}

export function min<T>(comparer: (x: T, y: T) => number, x: T, y: T) {
  return comparer(x, y) < 0 ? x : y;
}

export function max<T>(comparer: (x: T, y: T) => number, x: T, y: T) {
  return comparer(x, y) > 0 ? x : y;
}

export function createAtom<T>(value?: T): (v?: T, isSet?: boolean) => T | void {
  let atom = value;
  return (value?: T, isSetter?: boolean) => {
    if (!isSetter) {
      return atom;
    } else {
      atom = value;
      return void 0;
    }
  };
}

export function createObj(fields: Iterable<[string, any]>) {
  const obj: any = {};
  for (const kv of fields) {
    obj[kv[0]] = kv[1];
  }
  return obj;
}

export function createObjDebug(fields: Iterable<[string, any]>) {
  const obj: any = {};
  for (const kv of fields) {
    if (kv[0] in obj) {
      console.error(new Error(`Property ${kv[0]} is duplicated`));
    }
    obj[kv[0]] = kv[1];
  }
  return obj;
}

export function jsOptions(mutator: (x: object) => void): object {
  const opts = {};
  mutator(opts);
  return opts;
}

export function round(value: number, digits: number = 0) {
  const m = Math.pow(10, digits);
  const n = +(digits ? value * m : value).toFixed(8);
  const i = Math.floor(n);
  const f = n - i;
  const e = 1e-8;
  const r = (f > 0.5 - e && f < 0.5 + e) ? ((i % 2 === 0) ? i : i + 1) : Math.round(n);
  return digits ? r / m : r;
}

export function sign(x: number): number {
  return x > 0 ? 1 : x < 0 ? -1 : 0;
}

export function randomNext(min: number, max: number) {
  return Math.floor(Math.random() * (max - min)) + min;
}

export function randomBytes(buffer: Uint8Array) {
  if (buffer == null) { throw new Error("Buffer cannot be null"); }
  for (let i = 0; i < buffer.length; i += 6) {
    // Pick random 48-bit number. Fill buffer in 2 24-bit chunks to avoid bitwise truncation.
    let r = Math.floor(Math.random() * 281474976710656); // Low 24 bits = chunk 1.
    const rhi = Math.floor(r / 16777216); // High 24 bits shifted via division = chunk 2.
    for (let j = 0; j < 6 && i + j < buffer.length; j++) {
      if (j === 3) { r = rhi; }
      buffer[i + j] = r & 255;
      r >>>= 8;
    }
  }
}

export function unescapeDataString(s: string): string {
  // https://stackoverflow.com/a/4458580/524236
  return decodeURIComponent((s).replace(/\+/g, "%20"));
}
export function escapeDataString(s: string): string {
  return encodeURIComponent(s).replace(/!/g, "%21")
    .replace(/'/g, "%27")
    .replace(/\(/g, "%28")
    .replace(/\)/g, "%29")
    .replace(/\*/g, "%2A");
}
export function escapeUriString(s: string): string {
  return encodeURI(s);
}

// ICollection.Clear and Count members can be called on Arrays
// or Dictionaries so we need a runtime check (see #1120)
export function count<T>(col: Iterable<T>): number {
  if (isArrayLike(col)) {
    return col.length;
  } else {
    let count = 0;
    for (const _ of col) {
      count++;
    }
    return count;
  }
}

export function clear<T>(col: Iterable<T>) {
  if (isArrayLike(col)) {
    (col as any as T[]).splice(0);
  } else {
    (col as any).clear();
  }
}

const CURRIED_KEY = "__CURRIED__";

export function uncurry(arity: number, f: Function) {
  // f may be a function option with None value
  if (f == null) { return undefined; }

  // The function is already uncurried
  if (f.length > 1) {
    // if (CURRIED_KEY in f) { // This doesn't always work
    return f;
  }

  let uncurriedFn: Function;
  switch (arity) {
    case 2:
      uncurriedFn = (a1: any, a2: any) => f(a1)(a2);
      break;
    case 3:
      uncurriedFn = (a1: any, a2: any, a3: any) => f(a1)(a2)(a3);
      break;
    case 4:
      uncurriedFn = (a1: any, a2: any, a3: any, a4: any) => f(a1)(a2)(a3)(a4);
      break;
    case 5:
      uncurriedFn = (a1: any, a2: any, a3: any, a4: any, a5: any) => f(a1)(a2)(a3)(a4)(a5);
      break;
    case 6:
      uncurriedFn = (a1: any, a2: any, a3: any, a4: any, a5: any, a6: any) => f(a1)(a2)(a3)(a4)(a5)(a6);
      break;
    case 7:
      uncurriedFn = (a1: any, a2: any, a3: any, a4: any, a5: any, a6: any, a7: any) =>
        f(a1)(a2)(a3)(a4)(a5)(a6)(a7);
      break;
    case 8:
      uncurriedFn = (a1: any, a2: any, a3: any, a4: any, a5: any, a6: any, a7: any, a8: any) =>
        f(a1)(a2)(a3)(a4)(a5)(a6)(a7)(a8);
      break;
    default:
      throw new Error("Uncurrying to more than 8-arity is not supported: " + arity);
  }
  (uncurriedFn as any)[CURRIED_KEY] = f;
  return uncurriedFn;
}

export function curry(arity: number, f: Function): Function | undefined {
  if (f == null) { return undefined; }
  if (CURRIED_KEY in f) {
    return (f as any)[CURRIED_KEY];
  }
  switch (arity) {
    case 2:
      return (a1: any) => (a2: any) => f(a1, a2);
    case 3:
      return (a1: any) => (a2: any) => (a3: any) => f(a1, a2, a3);
    case 4:
      return (a1: any) => (a2: any) => (a3: any) => (a4: any) => f(a1, a2, a3, a4);
    case 5:
      return (a1: any) => (a2: any) => (a3: any) =>
        (a4: any) => (a5: any) => f(a1, a2, a3, a4, a5);
    case 6:
      return (a1: any) => (a2: any) => (a3: any) => (a4: any) =>
        (a5: any) => (a6: any) => f(a1, a2, a3, a4, a5, a6);
    case 7:
      return (a1: any) => (a2: any) => (a3: any) => (a4: any) => (a5: any) =>
        (a6: any) => (a7: any) => f(a1, a2, a3, a4, a5, a6, a7);
    case 8:
      return (a1: any) => (a2: any) => (a3: any) => (a4: any) => (a5: any) => (a6: any) =>
        (a7: any) => (a8: any) => f(a1, a2, a3, a4, a5, a6, a7, a8);
    default:
      throw new Error("Currying to more than 8-arity is not supported: " + arity);
  }
}

export function partialApply(arity: number, f: Function, args: any[]): any {
  if (f == null) {
    return undefined;
  } else if (CURRIED_KEY in f) {
    f = (f as any)[CURRIED_KEY];
    for (let i = 0; i < args.length; i++) {
      f = f(args[i]);
    }
    return f;
  } else {
    switch (arity) {
      case 1:
        // Wrap arguments to make sure .concat doesn't destruct arrays. Example
        // [1,2].concat([3,4],5)   --> [1,2,3,4,5]    // fails
        // [1,2].concat([[3,4],5]) --> [1,2,[3,4],5]  // ok
        return (a1: any) => f.apply(undefined, args.concat([a1]));
      case 2:
        return (a1: any) => (a2: any) => f.apply(undefined, args.concat([a1, a2]));
      case 3:
        return (a1: any) => (a2: any) => (a3: any) => f.apply(undefined, args.concat([a1, a2, a3]));
      case 4:
        return (a1: any) => (a2: any) => (a3: any) => (a4: any) => f.apply(undefined, args.concat([a1, a2, a3, a4]));
      case 5:
        return (a1: any) => (a2: any) => (a3: any) =>
          (a4: any) => (a5: any) => f.apply(undefined, args.concat([a1, a2, a3, a4, a5]));
      case 6:
        return (a1: any) => (a2: any) => (a3: any) => (a4: any) =>
          (a5: any) => (a6: any) => f.apply(undefined, args.concat([a1, a2, a3, a4, a5, a6]));
      case 7:
        return (a1: any) => (a2: any) => (a3: any) => (a4: any) => (a5: any) =>
          (a6: any) => (a7: any) => f.apply(undefined, args.concat([a1, a2, a3, a4, a5, a6, a7]));
      case 8:
        return (a1: any) => (a2: any) => (a3: any) => (a4: any) => (a5: any) => (a6: any) =>
          (a7: any) => (a8: any) => f.apply(undefined, args.concat([a1, a2, a3, a4, a5, a6, a7, a8]));
      default:
        throw new Error("Partially applying to more than 8-arity is not supported: " + arity);
    }
  }
}

type CurriedArgMapping = [number, number] | 0; // expected arity, actual arity

export function mapCurriedArgs(fn: Function, mappings: CurriedArgMapping[]) {
  function mapArg(fn: Function, arg: any, mappings: CurriedArgMapping[], idx: number) {
    const mapping = mappings[idx];
    if (mapping !== 0) {
      const expectedArity = mapping[0];
      const actualArity = mapping[1];
      if (expectedArity > 1) {
        arg = curry(expectedArity, arg);
      }
      if (actualArity > 1) {
        arg = uncurry(actualArity, arg);
      }
    }
    const res = fn(arg);
    if (idx + 1 === mappings.length) {
      return res;
    } else {
      return (arg: any) => mapArg(res, arg, mappings, idx + 1);
    }
  }
  return (arg: any) => mapArg(fn, arg, mappings, 0);
}

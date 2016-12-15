import * as Long from "./Long"

const littleEndian = true;

function isLittleEndian() {
    return littleEndian;
}
function getBooleanBytes(value: boolean) {
    let bytes = new Uint8Array(1);
    new DataView(bytes.buffer).setUint8(0, value ? 1 : 0);
    return bytes;
}
function getCharBytes(value: string) {
    let bytes = new Uint8Array(2);
    new DataView(bytes.buffer).setUint16(0, value.charCodeAt(0), littleEndian);
    return bytes;
}
function getInt16Bytes(value: number) {
    let bytes = new Uint8Array(2);
    new DataView(bytes.buffer).setInt16(0, value, littleEndian);
    return bytes;
}
function getInt32Bytes(value: number) {
    let bytes = new Uint8Array(4);
    new DataView(bytes.buffer).setInt32(0, value, littleEndian);
    return bytes;
}
function getInt64Bytes(value: Long.Long) {
    let bytes = new Uint8Array(8);
    new DataView(bytes.buffer).setInt32(littleEndian ? 0 : 4, value.getLowBits(), littleEndian);
    new DataView(bytes.buffer).setInt32(littleEndian ? 4 : 0, value.getHighBits(), littleEndian);
    return bytes;
}
function getUInt16Bytes(value: number) {
    let bytes = new Uint8Array(2);
    new DataView(bytes.buffer).setUint16(0, value, littleEndian);
    return bytes;
}
function getUInt32Bytes(value: number) {
    let bytes = new Uint8Array(4);
    new DataView(bytes.buffer).setUint32(0, value, littleEndian);
    return bytes;
}
function getUInt64Bytes(value: Long.Long) {
    let bytes = new Uint8Array(8);
    new DataView(bytes.buffer).setUint32(littleEndian ? 0 : 4, value.getLowBitsUnsigned(), littleEndian);
    new DataView(bytes.buffer).setUint32(littleEndian ? 4 : 0, value.getHighBitsUnsigned(), littleEndian);
    return bytes;
}
function getSingleBytes(value: number) {
    let bytes = new Uint8Array(4);
    new DataView(bytes.buffer).setFloat32(0, value, littleEndian);
    return bytes;
}
function getDoubleBytes(value: number) {
    let bytes = new Uint8Array(8);
    new DataView(bytes.buffer).setFloat64(0, value, littleEndian);
    return bytes;
}
function int64BitsToDouble(value: Long.Long) {
    let buffer = new ArrayBuffer(8);
    new DataView(buffer).setInt32(littleEndian ? 0 : 4, value.getLowBits(), littleEndian);
    new DataView(buffer).setInt32(littleEndian ? 4 : 0, value.getHighBits(), littleEndian);
    return new DataView(buffer).getFloat64(0, littleEndian);
}
function doubleToInt64Bits(value: number) {
    let buffer = new ArrayBuffer(8);
    new DataView(buffer).setFloat64(0, value, littleEndian);
    let lowBits = new DataView(buffer).getInt32(littleEndian ? 0 : 4, littleEndian);
    let highBits = new DataView(buffer).getInt32(littleEndian ? 4 : 0, littleEndian);
    return Long.fromBits(lowBits, highBits, false);
}
function toBoolean(bytes: Uint8Array, offset: number): boolean {
    return new DataView(bytes.buffer).getUint8(offset) === 1 ? true : false;
}
function toChar(bytes: Uint8Array, offset: number) {
    let code = new DataView(bytes.buffer).getUint16(offset, littleEndian);
    return String.fromCharCode(code);
}
function toInt16(bytes: Uint8Array, offset: number) {
    return new DataView(bytes.buffer).getInt16(offset, littleEndian);
}
function toInt32(bytes: Uint8Array, offset: number) {
    return new DataView(bytes.buffer).getInt32(offset, littleEndian);
}
function toInt64(bytes: Uint8Array, offset: number) {
    let lowBits = new DataView(bytes.buffer).getInt32(offset + (littleEndian ? 0 : 4), littleEndian);
    let highBits = new DataView(bytes.buffer).getInt32(offset + (littleEndian ? 4 : 0), littleEndian);
    return Long.fromBits(lowBits, highBits, false);
}
function toUInt16(bytes: Uint8Array, offset: number) {
    return new DataView(bytes.buffer).getUint16(offset, littleEndian);
}
function toUInt32(bytes: Uint8Array, offset: number) {
    return new DataView(bytes.buffer).getUint32(offset, littleEndian);
}
function toUInt64(bytes: Uint8Array, offset: number) {
    let lowBits = new DataView(bytes.buffer).getUint32(offset + (littleEndian ? 0 : 4), littleEndian);
    let highBits = new DataView(bytes.buffer).getUint32(offset + (littleEndian ? 4 : 0), littleEndian);
    return Long.fromBits(lowBits, highBits, true);
}
function toSingle(bytes: Uint8Array, offset: number) {
    return new DataView(bytes.buffer).getFloat32(offset, littleEndian);
}
function toDouble(bytes: Uint8Array, offset: number) {
    return new DataView(bytes.buffer).getFloat64(offset, littleEndian);
}
function toString(bytes: Uint8Array, offset?: number, count?: number) {
    let ar = bytes;
    if (typeof offset !== "undefined" && typeof count !== "undefined")
        ar = bytes.subarray(offset, offset + count)
    else if (typeof offset !== "undefined")
        ar = bytes.subarray(offset);
    return Array.from(ar).map(b => b.toString(16)).join("-");
}


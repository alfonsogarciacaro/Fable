// ignore_for_file: file_names

import 'dart:math';

abstract class IRandom {
  int next([int? minValue, int? maxValue]);
  double nextDouble();
  void nextBytes(List<int> buffer);
}

class RandomImpl implements IRandom {
  final Random _random;

  RandomImpl([int? seed]) : _random = seed != null ? Random(seed) : Random();

  @override
  double nextDouble() {
    return _random.nextDouble();
  }

  @override
  void nextBytes(List<int> buffer) {
    for (int i = 0; i < buffer.length; i++) {
        buffer[i] = _random.nextInt(256);
      }
  }

  @override
  int next([int? minValue, int? maxValue]) {
    if (maxValue == null) {
      if (minValue == null) {
        return _random.nextInt(1 << 31); // Generate a random 31-bit integer
      } else {
        maxValue = minValue;
        if (maxValue <= 0) {
          throw ArgumentError("maxValue must be greater than 0");
        }
        return _random.nextInt(maxValue);
      }
    } else {
      minValue = minValue!;
      if (maxValue <= minValue) {
        throw ArgumentError("minValue must be less than maxValue");
      }
      return minValue + _random.nextInt(maxValue - minValue);
    }
  }
}

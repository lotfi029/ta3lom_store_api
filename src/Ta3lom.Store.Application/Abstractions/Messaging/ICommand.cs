namespace Ta3lom.Store.Application.Abstractions.Messaging;

/// A use case that changes state. One transaction per command.
public interface ICommand : IBaseCommand;

/// A use case that changes state and returns a value.
public interface ICommand<TResponse> : IBaseCommand;

public interface IBaseCommand;

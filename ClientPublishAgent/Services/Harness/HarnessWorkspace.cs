// ...existing code...
			ChatOptions = new ChatOptions
			{
				Instructions = BuildInstructions(settings.Profile),
				Tools = tools
			}
		});
		#pragma warning restore MAAI001
	}

	private static string BuildInstructions(AgentProfile? profile)
	{
		var instructions = profile?.Instructions ?? "You are a general personal assistant. Reply in the user's language. Never claim a task succeeded unless its tool reports success.";
		if (profile?.Terminal == true)
			instructions += "\n本地 run_terminal 的命令输出只保存在本机，不会发送给模型。不要把输出写入文件、不要用文件工具读取终端日志、不要重复或改写命令来试图取得输出；若无法回答，明确告诉用户这一限制并停止。用户询问本机操作系统/版本信息时，使用 get_computer_version_info 一次获取，不要用终端循环探测。";
		return instructions;
	}
// ...existing code...